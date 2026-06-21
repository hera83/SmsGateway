namespace api.Dtos.Errors;

public sealed class NotFoundErrorDto : ErrorResponseDto
{
    public NotFoundErrorDto()
    {
        Code = "not_found";
        Status = StatusCodes.Status404NotFound;
    }
}
