export const SITE_PROOF_PROTOCOL = 'tezuri.site-proof-run' as const
export const SITE_PROOF_PROTOCOL_VERSION = 1 as const

export type SiteProofStatusV1 = 'passed' | 'failed' | 'timed-out' | 'start-failed'

export interface SiteProofRunReceiptV1 {
  readonly protocol: typeof SITE_PROOF_PROTOCOL
  readonly version: typeof SITE_PROOF_PROTOCOL_VERSION
  readonly runId: string
  readonly status: SiteProofStatusV1
  readonly startedAt: string
  readonly completedAt: string
  readonly progress: SiteProofProgressV1
  readonly result: SiteProofResultV1
}

export interface SiteProofProgressV1 {
  readonly state: SiteProofStatusV1
  readonly completedCommands: number
  readonly totalCommands: number
  readonly currentCommandId: string | null
}

export interface SiteProofResultV1 {
  readonly succeeded: boolean
  readonly commands: readonly SiteProofCommandResultV1[]
}

export interface SiteProofCommandResultV1 {
  readonly id: string
  readonly executable: string
  readonly arguments: readonly string[]
  readonly status: SiteProofStatusV1
  readonly exitCode: number | null
  readonly timedOut: boolean
  readonly durationMilliseconds: number
  readonly standardOutput: string
  readonly standardError: string
  readonly standardOutputTruncated: boolean
  readonly standardErrorTruncated: boolean
  readonly outputDirectory: string | null
  readonly outputDirectoryExists: boolean
}
