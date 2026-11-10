/**
 * The SDK of Reliquary plugins. See the README file of this package.
 */

export { definePlugin } from './plugin.ts'
export type { DefinePluginOptions, PanelDefinition, PluginDefinition, PluginEnvironment, PluginHandle, PluginSdk } from './plugin.ts'
export { RpcError, RpcPeer, utf8Length } from './rpc.ts'
export type { CallOptions, MethodMap, PeerOptions, PortLike } from './rpc.ts'
export * from './protocol.ts'
export type * from './methods.ts'
export * from './manifest.ts'
export * from './slots.ts'
