namespace AcmePay.Domain.Payments;

public enum PaymentStatus
{
    Pending = 0,
    Succeeded = 1,
    Declined = 2,
    Refunded = 3,
    PartiallyRefunded = 4
}
