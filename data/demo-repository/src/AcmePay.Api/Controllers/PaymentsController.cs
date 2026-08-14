using AcmePay.Application.Payments;
using AcmePay.Domain.Payments;
using Microsoft.AspNetCore.Mvc;

namespace AcmePay.Api.Controllers;

[ApiController]
[Route("api/v1/payments")]
public sealed class PaymentsController(ProcessPaymentHandler processPayment) : ControllerBase
{
    /// <summary>Creates and charges a payment. Returns 201 with the payment id.</summary>
    [HttpPost]
    [ProducesResponseType<PaymentResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Create(
        [FromBody] ProcessPaymentCommand command, CancellationToken ct)
    {
        var result = await processPayment.HandleAsync(command, ct);
        return result.Status switch
        {
            PaymentStatus.Succeeded => StatusCode(StatusCodes.Status201Created, result),
            _ => BadRequest(result)
        };
    }

    [HttpGet("{paymentId:guid}")]
    [ProducesResponseType<PaymentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid paymentId, CancellationToken ct)
    {
        var result = await processPayment.GetAsync(paymentId, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
