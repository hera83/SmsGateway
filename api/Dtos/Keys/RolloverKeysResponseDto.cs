namespace api.Dtos.Keys;

public sealed class RolloverKeysResponseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public decimal Balance { get; set; }
    public string ResponsibleName { get; set; } = string.Empty;
    public string ResponsibleEmail { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string ApiKey { get; set; } = string.Empty;
}
