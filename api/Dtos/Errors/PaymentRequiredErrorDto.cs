namespace api.Dtos.Errors;

public sealed class PaymentRequiredErrorDto : ErrorResponseDto
{
    public PaymentRequiredErrorDto()
    {
        Code = "payment_required";
        Status = StatusCodes.Status402PaymentRequired;
    }
}
