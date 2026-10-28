import axe from 'axe-core'

// axe tries a canvas for some checks. jsdom has no canvas and writes an error for each call.
HTMLCanvasElement.prototype.getContext = (() => null) as typeof HTMLCanvasElement.prototype.getContext

/**
 * Runs the axe accessibility checks on the document.
 * jsdom has no layout, so the check of the color contrast cannot run here. The test of the theme tokens checks the contrast instead.
 * @returns One line for each problem with the impact "serious" or "critical": the rule, the help text and the elements.
 */
export async function seriousAccessibilityProblems(context: Element | Document = document): Promise<string[]> {
  const results = await axe.run(context, {
    resultTypes: ['violations'],
    rules: { 'color-contrast': { enabled: false } },
  })
  return results.violations
    .filter((violation) => violation.impact === 'serious' || violation.impact === 'critical')
    .map((violation) => `${violation.id} (${violation.impact}): ${violation.help} ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`)
}

/** Gives all the problems that axe finds, for the investigation of a failed test. */
export async function allAccessibilityProblems(context: Element | Document = document): Promise<string[]> {
  const results = await axe.run(context, { resultTypes: ['violations'], rules: { 'color-contrast': { enabled: false } } })
  return results.violations.map((violation) => `${violation.id} (${violation.impact}): ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`)
}
