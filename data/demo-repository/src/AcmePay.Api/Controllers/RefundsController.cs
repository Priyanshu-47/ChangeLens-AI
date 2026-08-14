using AcmePay.Application.Payments;
using Microsoft.AspNetCore.Mvc;

namespace AcmePay.Api.Controllers;

[ApiController]
[Route("api/v1/payments/{paymentId:guid}/refunds")]
public sealed class RefundsController(RefundPaymentHandler refundPayment) : ControllerBase
{
    /// <summary>Issues a full or partial refund for a succeeded payment.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Refund(
        Guid paymentId, [FromBody] RefundPaymentCommand command, CancellationToken ct)
    {
        var refund = await refundPayment.HandleAsync(paymentId, command, ct);
        return StatusCode(StatusCodes.Status201Created, refund);
    }
}
