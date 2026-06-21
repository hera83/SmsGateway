namespace api.Dtos.Errors;

public sealed class BadRequestErrorDto : ErrorResponseDto
{
    public BadRequestErrorDto()
    {
        Code = "bad_request";
        Status = StatusCodes.Status400BadRequest;
    }
}
