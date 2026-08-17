export type AdvisoryValidationIssue = {
  path: string
  message: string
}

export function validateAdvisorySchema(value: unknown, schema: unknown): AdvisoryValidationIssue[] {
  const issues: AdvisoryValidationIssue[] = []
  validate(value, schema, '$', issues)
  return issues
}

function validate(value: unknown, schema: unknown, path: string, issues: AdvisoryValidationIssue[]) {
  if (!schema || typeof schema !== 'object' || Array.isArray(schema)) {
    issues.push({ path: '$schema', message: 'The schema root must be a JSON object.' })
    return
  }

  const record = schema as Record<string, unknown>
  if ('const' in record && !sameJson(value, record.const)) {
    issues.push({ path, message: 'Value does not match const.' })
  }

  if ('enum' in record) {
    if (!Array.isArray(record.enum) || !record.enum.some((item) => sameJson(value, item))) {
      issues.push({ path, message: 'Value is not included in enum.' })
    }
  }

  if ('type' in record) {
    const types = Array.isArray(record.type) ? record.type : [record.type]
    if (!types.every((item) => typeof item === 'string')) {
      issues.push({ path: '$schema.type', message: 'Schema type must be a string or an array of strings.' })
    } else if (!types.some((type) => matchesType(value, type))) {
      issues.push({ path, message: `Expected type ${types.join(' or ')}.` })
      return
    }
  }

  if (isObject(value)) {
    const required = record.required
    if (Array.isArray(required)) {
      for (const key of required) {
        if (typeof key === 'string' && !(key in value)) {
          issues.push({ path: `${path}.${key}`, message: 'Required property is missing.' })
        }
      }
    }

    const properties = record.properties
    if (properties && isObject(properties)) {
      for (const [key, childSchema] of Object.entries(properties)) {
        if (key in value) validate(value[key], childSchema, `${path}.${key}`, issues)
      }
    }

    if (record.additionalProperties === false && properties && isObject(properties)) {
      for (const key of Object.keys(value)) {
        if (!(key in properties)) issues.push({ path: `${path}.${key}`, message: 'Additional property is not allowed.' })
      }
    }
  }

  if (Array.isArray(value) && record.items !== undefined) {
    for (let index = 0; index < value.length; index++) {
      validate(value[index], record.items, `${path}[${index}]`, issues)
    }
  }
}

function matchesType(value: unknown, type: string) {
  switch (type) {
    case 'object': return isObject(value)
    case 'array': return Array.isArray(value)
    case 'string': return typeof value === 'string'
    case 'number': return typeof value === 'number' && Number.isFinite(value)
    case 'integer': return typeof value === 'number' && Number.isInteger(value)
    case 'boolean': return typeof value === 'boolean'
    case 'null': return value === null
    default: return false
  }
}

function isObject(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value)
}

function sameJson(left: unknown, right: unknown) {
  return JSON.stringify(left) === JSON.stringify(right)
}
