using Microsoft.AspNetCore.Mvc;
using Tezuri.Domain.Proof;
using Tezuri.Infrastructure.Proof;

namespace Tezuri.Controllers;

[ApiController]
[Route("api/v1/proof/runs")]
public sealed class ProofRunsController(SiteProofRunner runner) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<SiteProofRunReceiptV1>(StatusCodes.Status200OK)]
    // The request deliberately carries no executable or arguments. Execution authority
    // comes only from the validated, committed WorkspaceConfigurationV1 singleton.
    public async Task<ActionResult<SiteProofRunReceiptV1>> Run(CancellationToken cancellationToken) =>
        Ok(await runner.RunAsync(cancellationToken));
}
