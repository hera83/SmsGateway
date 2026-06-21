using api.Data;
using api.Dtos.Errors;
using api.Dtos.Logs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
[Authorize(Policy = "MasterKeyOnly")]
public class LogsController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public LogsController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(SearchLogsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(UnauthorizedErrorDto), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ForbiddenErrorDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SearchLogsResponseDto>> Search([FromQuery] SearchLogsRequestDto request)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);

        var query = _dbContext.LogEntries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Level))
        {
            query = query.Where(x => x.Level == request.Level);
        }

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            query = query.Where(x =>
                x.Message.Contains(request.Q) ||
                (x.Exception != null && x.Exception.Contains(request.Q)) ||
                (x.SourceContext != null && x.SourceContext.Contains(request.Q)) ||
                (x.PropertiesJson != null && x.PropertiesJson.Contains(request.Q)));
        }

        if (request.From.HasValue)
        {
            query = query.Where(x => x.Timestamp >= request.From.Value);
        }

        if (request.To.HasValue)
        {
            query = query.Where(x => x.Timestamp <= request.To.Value);
        }

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(x => x.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new SearchLogsItemResponseDto
            {
                Id = x.Id,
                Timestamp = x.Timestamp,
                Level = x.Level,
                Message = x.Message,
                MessageTemplate = x.MessageTemplate,
                Exception = x.Exception,
                SourceContext = x.SourceContext,
                PropertiesJson = x.PropertiesJson
            })
            .ToListAsync();

        return Ok(new SearchLogsResponseDto
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            Items = items
        });
    }
}
