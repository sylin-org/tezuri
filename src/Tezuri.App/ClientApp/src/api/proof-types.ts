export type ProofStatus = 'passed' | 'failed' | 'timed-out' | 'start-failed'

export interface ProofRun {
  readonly runId: string
  readonly status: ProofStatus
  readonly startedAt: string
  readonly completedAt: string
  readonly progress: ProofProgress
  readonly result: ProofResult
}

export interface ProofProgress {
  readonly state: ProofStatus
  readonly completedCommands: number
  readonly totalCommands: number
  readonly currentCommandId: string | null
}

export interface ProofResult {
  readonly succeeded: boolean
  readonly commands: readonly ProofCommandResult[]
}

export interface ProofCommandResult {
  readonly id: string
  readonly executable: string
  readonly arguments: readonly string[]
  readonly status: ProofStatus
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
