import { createHash } from 'node:crypto'
import { readdirSync, readFileSync, writeFileSync } from 'node:fs'
import { join, relative, resolve, sep } from 'node:path'
import type { Plugin, ResolvedConfig } from 'vite'

const workerSource = new URL('./src/pwa/service-worker.js', import.meta.url)

/** Gives the paths of all the files in a folder and its subfolders. */
function filesIn(folder: string): string[] {
  try {
    return readdirSync(folder, { recursive: true, withFileTypes: true })
      .filter((entry) => entry.isFile())
      .map((entry) => join(entry.parentPath, entry.name))
  } catch {
    return []
  }
}

/**
 * Writes the service worker to dist/sw.js after the build.
 * The worker gets the list of the files of the application shell and a version that changes with each build.
 * See docs/development/web-app.md.
 */
export function serviceWorker(): Plugin {
  let config: ResolvedConfig
  return {
    name: 'reliquary-service-worker',
    apply: 'build',
    configResolved(resolved) {
      config = resolved
    },
    // The hook runs after Vite writes all the files, so the bundle also contains index.html.
    writeBundle(options, bundle) {
      const hash = createHash('sha256')
      const files: string[] = []

      for (const [fileName, output] of Object.entries(bundle).sort(([left], [right]) => left.localeCompare(right))) {
        if (fileName.endsWith('.map')) {
          continue
        }
        files.push(`/${fileName}`)
        hash.update(fileName)
        hash.update(output.type === 'chunk' ? output.code : output.source)
      }

      const publicDir = config.publicDir
      for (const path of filesIn(publicDir).sort()) {
        const fileName = relative(publicDir, path).split(sep).join('/')
        files.push(`/${fileName}`)
        hash.update(fileName)
        hash.update(readFileSync(path))
      }

      const source = readFileSync(workerSource, 'utf8')
      hash.update(source)
      const version = hash.digest('hex').slice(0, 16)

      const outDir = options.dir ?? resolve(config.root, config.build.outDir)
      writeFileSync(join(outDir, 'sw.js'), `self.__buildVersion = ${JSON.stringify(version)}\nself.__buildFiles = ${JSON.stringify(files)}\n${source}`)
    },
  }
}
