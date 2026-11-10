import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { describe, it } from 'node:test'
import {
  accountAuthMethods,
  accountSyncModes,
  formatDirections,
  formatTargets,
  manifestDevices,
  manifestPermissions,
  panelSlots,
  type AccountSync,
  type ManifestAccount,
  type ManifestFormat,
  type ManifestPanel,
  type PluginManifest,
} from './manifest.ts'

interface SchemaNode {
  properties?: Record<string, SchemaNode>
  required?: string[]
  items?: SchemaNode
  enum?: string[]
}

const schema = JSON.parse(readFileSync(new URL('../schema/manifest.schema.json', import.meta.url), 'utf8')) as SchemaNode

/** The fields of each type. The compiler checks that each list has all the fields of its type, and no other field. */
type Fields<T> = { [Key in keyof Required<T>]: undefined extends T[Key] ? 'optional' : 'required' }

const pluginManifestFields = {
  $schema: 'optional',
  id: 'required',
  kind: 'optional',
  name: 'required',
  description: 'optional',
  version: 'required',
  coreApi: 'required',
  entry: 'required',
  permissions: 'optional',
  slots: 'optional',
  network: 'optional',
  devices: 'optional',
  accounts: 'optional',
  panels: 'optional',
  formats: 'optional',
  i18n: 'optional',
  settings: 'optional',
} as const satisfies Fields<PluginManifest>

const accountFields = { site: 'required', auth: 'required', sync: 'optional' } as const satisfies Fields<ManifestAccount>
const syncFields = { mode: 'required', intervalMinutes: 'optional' } as const satisfies Fields<AccountSync>
const panelFields = { id: 'required', slot: 'required', title: 'required' } as const satisfies Fields<ManifestPanel>
const formatFields = { id: 'required', direction: 'required', target: 'required', title: 'required', extensions: 'optional' } as const satisfies Fields<ManifestFormat>

function assertFields(node: SchemaNode, fields: Record<string, 'optional' | 'required'>): void {
  assert.deepEqual(Object.keys(fields).sort(), Object.keys(node.properties ?? {}).sort(), 'The type and the schema have different fields.')
  const required = Object.entries(fields).filter(([, value]) => value === 'required').map(([key]) => key)
  assert.deepEqual(required.sort(), [...(node.required ?? [])].sort(), 'The type and the schema have different required fields.')
}

describe('manifest types', () => {
  const properties = schema.properties!
  const account = properties.accounts.items!
  const panel = properties.panels.items!
  const format = properties.formats.items!

  it('have the fields of the schema', () => {
    assertFields(schema, pluginManifestFields)
    assertFields(account, accountFields)
    assertFields(account.properties!.sync, syncFields)
    assertFields(panel, panelFields)
    assertFields(format, formatFields)
  })

  it('have the values of the schema', () => {
    assert.deepEqual([...manifestPermissions], properties.permissions.items!.enum)
    assert.deepEqual([...manifestDevices], properties.devices.items!.enum)
    assert.deepEqual(properties.kind.enum, ['plugin'])
    assert.deepEqual([...accountAuthMethods], account.properties!.auth.enum)
    assert.deepEqual([...accountSyncModes], account.properties!.sync.properties!.mode.enum)
    assert.deepEqual([...panelSlots], panel.properties!.slot.enum)
    assert.deepEqual([...formatDirections], format.properties!.direction.enum)
    assert.deepEqual([...formatTargets], format.properties!.target.enum)
  })
})
