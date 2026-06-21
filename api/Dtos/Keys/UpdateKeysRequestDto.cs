namespace api.Dtos.Keys;

public sealed class UpdateKeysRequestDto
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public bool? IsActive { get; set; }
    public decimal? Balance { get; set; }
    public string? ResponsibleName { get; set; }
    public string? ResponsibleEmail { get; set; }
}
