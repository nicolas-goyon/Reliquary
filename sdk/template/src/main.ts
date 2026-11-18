import { definePlugin } from 'reliquary-plugin-sdk'
import { plugin } from './plugin.ts'

// The entry point of the plugin. The core loads this module in the plugin frame.
definePlugin(plugin)
