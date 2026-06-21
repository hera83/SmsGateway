using api.Data;
using api.Dtos.Cost;
using api.Dtos.Errors;
using api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace api.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
[Authorize]
public class CostController(AppDbContext dbContext) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(GetCurrentCostResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(UnauthorizedErrorDto), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ForbiddenErrorDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GetCurrentCostResponseDto>> GetCurrent(CancellationToken cancellationToken)
    {
        var current = await dbContext.CostConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (current is null)
        {
            return Ok(new GetCurrentCostResponseDto
            {
                SmsPriceDkk = 0m,
                UpdatedAt = DateTime.Now
            });
        }

        return Ok(new GetCurrentCostResponseDto
        {
            SmsPriceDkk = current.SmsPriceDkk,
            UpdatedAt = current.UpdatedAt
        });
    }

    [HttpPut]
    [Authorize(Policy = "MasterKeyOnly")]
    [ProducesResponseType(typeof(UpdateCostResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(BadRequestErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ValidationErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(UnauthorizedErrorDto), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ForbiddenErrorDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<UpdateCostResponseDto>> Update([FromBody] UpdateCostRequestDto request, CancellationToken cancellationToken)
    {
        if (request.SmsPriceDkk < 0)
        {
            return BadRequest(new BadRequestErrorDto
            {
                Message = "Sms price cannot be negative.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        var current = await dbContext.CostConfigurations.FirstOrDefaultAsync(cancellationToken);
        if (current is null)
        {
            current = new CostConfiguration
            {
                Id = Guid.NewGuid(),
                SmsPriceDkk = request.SmsPriceDkk,
                UpdatedAt = DateTime.Now
            };

            dbContext.CostConfigurations.Add(current);

            dbContext.CostPriceHistories.Add(new CostPriceHistory
            {
                Id = Guid.NewGuid(),
                OldSmsPriceDkk = 0m,
                NewSmsPriceDkk = request.SmsPriceDkk,
                ChangedAt = DateTime.Now
            });
        }
        else if (current.SmsPriceDkk != request.SmsPriceDkk)
        {
            dbContext.CostPriceHistories.Add(new CostPriceHistory
            {
                Id = Guid.NewGuid(),
                OldSmsPriceDkk = current.SmsPriceDkk,
                NewSmsPriceDkk = request.SmsPriceDkk,
                ChangedAt = DateTime.Now
            });

            current.SmsPriceDkk = request.SmsPriceDkk;
            current.UpdatedAt = DateTime.Now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new UpdateCostResponseDto
        {
            SmsPriceDkk = current.SmsPriceDkk,
            UpdatedAt = current.UpdatedAt
        });
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<GetHistoryCostResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(UnauthorizedErrorDto), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ForbiddenErrorDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<GetHistoryCostResponseDto>>> GetHistory(CancellationToken cancellationToken)
    {
        var history = await dbContext.CostPriceHistories
            .AsNoTracking()
            .OrderByDescending(x => x.ChangedAt)
            .Select(x => new GetHistoryCostResponseDto
            {
                Id = x.Id,
                OldSmsPriceDkk = x.OldSmsPriceDkk,
                NewSmsPriceDkk = x.NewSmsPriceDkk,
                ChangedAt = x.ChangedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(history);
    }

    [HttpPost]
    [ProducesResponseType(typeof(GetUsageReportCostResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(BadRequestErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(NotFoundErrorDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(UnauthorizedErrorDto), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ForbiddenErrorDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GetUsageReportCostResponseDto>> GetUsageReport([FromBody] GetUsageReportCostRequestDto request, CancellationToken cancellationToken)
    {
        var nameIdentifier = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid? apiKeyId = Guid.TryParse(nameIdentifier, out var parsedId) ? parsedId : null;

        if (request.PageNumber <= 0)
        {
            return BadRequest(new BadRequestErrorDto
            {
                Message = "PageNumber must be greater than 0.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        if (request.PageSize <= 0 || request.PageSize > 500)
        {
            return BadRequest(new BadRequestErrorDto
            {
                Message = "PageSize must be between 1 and 500.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        var query = dbContext.SmsRecords
            .AsNoTracking()
            .Where(x => x.Direction == SmsRecordDirections.Outgoing);

        if (apiKeyId.HasValue)
        {
            query = query.Where(x => x.ApiKeyId == apiKeyId.Value);
        }

        if (request.CreatedFrom.HasValue)
        {
            query = query.Where(x => x.CreatedAt >= request.CreatedFrom.Value);
        }

        if (request.CreatedTo.HasValue)
        {
            query = query.Where(x => x.CreatedAt <= request.CreatedTo.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            query = query.Where(x => x.Status == request.Status.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.ToPhoneNumber))
        {
            var toPhoneNumber = request.ToPhoneNumber.Trim();
            query = query.Where(x => x.ToPhoneNumber.Contains(toPhoneNumber));
        }

        if (!string.IsNullOrWhiteSpace(request.MessageContains))
        {
            var messageContains = request.MessageContains.Trim();
            query = query.Where(x => x.Message.Contains(messageContains));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalUsageDkk = await query.SumAsync(x => x.TotalPriceDkk, cancellationToken);

        var skip = (request.PageNumber - 1) * request.PageSize;
        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip(skip)
            .Take(request.PageSize)
            .Select(x => new GetUsageReportCostItemResponseDto
            {
                SmsId = x.Id,
                CreatedAt = x.CreatedAt,
                ApiKeyName = x.ApiKey != null ? x.ApiKey.Name : null,
                ToPhoneNumber = x.ToPhoneNumber,
                Message = x.Message,
                Status = x.Status,
                SegmentCount = x.SegmentCount,
                UnitPriceDkk = x.UnitPriceDkk,
                TotalPriceDkk = x.TotalPriceDkk,
                IsBalanceDeducted = x.IsBalanceDeducted
            })
            .ToListAsync(cancellationToken);

        return Ok(new GetUsageReportCostResponseDto
        {
            ApiKeyId = apiKeyId.HasValue ? apiKeyId.Value : null,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            TotalCount = totalCount,
            TotalUsageDkk = totalUsageDkk,
            Items = items
        });
    }

    [HttpGet]
    [ProducesResponseType(typeof(GetBalanceCostResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotFoundErrorDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(UnauthorizedErrorDto), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ForbiddenErrorDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GetBalanceCostResponseDto>> GetBalance(CancellationToken cancellationToken)
    {
        var nameIdentifier = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(nameIdentifier, out var apiKeyId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ForbiddenErrorDto
            {
                Message = "GetBalance requires a specific API key. Use GetGlobalBalance instead.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        var apiKey = await dbContext.ApiKeys
            .AsNoTracking()
            .Where(x => x.Id == apiKeyId)
            .Select(x => new { x.Id, x.Balance, x.UpdatedAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (apiKey is null)
        {
            return NotFound(new NotFoundErrorDto
            {
                Message = "API key not found.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        return Ok(new GetBalanceCostResponseDto
        {
            ApiKeyId = apiKey.Id,
            Balance = apiKey.Balance,
            UpdatedAt = apiKey.UpdatedAt
        });
    }

    [HttpGet]
    [Authorize(Policy = "MasterKeyOnly")]
    [ProducesResponseType(typeof(GetGlobalBalanceCostResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(UnauthorizedErrorDto), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ForbiddenErrorDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GetGlobalBalanceCostResponseDto>> GetGlobalBalance(CancellationToken cancellationToken)
    {
        var keys = await dbContext.ApiKeys
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new { x.Balance, x.UpdatedAt })
            .ToListAsync(cancellationToken);

        return Ok(new GetGlobalBalanceCostResponseDto
        {
            Balance = keys.Sum(x => x.Balance),
            KeyCount = keys.Count,
            UpdatedAt = keys.Max(x => x.UpdatedAt)
        });
    }

    [HttpPost]
    [Authorize(Policy = "MasterKeyOnly")]
    [ProducesResponseType(typeof(GetGlobalUsageReportCostResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(BadRequestErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ValidationErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(UnauthorizedErrorDto), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ForbiddenErrorDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GetGlobalUsageReportCostResponseDto>> GetGlobalUsageReport([FromBody] GetUsageReportCostRequestDto request, CancellationToken cancellationToken)
    {
        if (request.PageNumber <= 0)
        {
            return BadRequest(new BadRequestErrorDto
            {
                Message = "PageNumber must be greater than 0.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        if (request.PageSize <= 0 || request.PageSize > 500)
        {
            return BadRequest(new BadRequestErrorDto
            {
                Message = "PageSize must be between 1 and 500.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        var query = dbContext.SmsRecords
            .AsNoTracking()
            .Where(x => x.Direction == SmsRecordDirections.Outgoing);

        if (request.CreatedFrom.HasValue)
        {
            query = query.Where(x => x.CreatedAt >= request.CreatedFrom.Value);
        }

        if (request.CreatedTo.HasValue)
        {
            query = query.Where(x => x.CreatedAt <= request.CreatedTo.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            query = query.Where(x => x.Status == request.Status.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.ToPhoneNumber))
        {
            var toPhoneNumber = request.ToPhoneNumber.Trim();
            query = query.Where(x => x.ToPhoneNumber.Contains(toPhoneNumber));
        }

        if (!string.IsNullOrWhiteSpace(request.MessageContains))
        {
            var messageContains = request.MessageContains.Trim();
            query = query.Where(x => x.Message.Contains(messageContains));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalUsageDkk = await query.SumAsync(x => x.TotalPriceDkk, cancellationToken);

        var skip = (request.PageNumber - 1) * request.PageSize;
        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip(skip)
            .Take(request.PageSize)
            .Select(x => new GetUsageReportCostItemResponseDto
            {
                SmsId = x.Id,
                CreatedAt = x.CreatedAt,
                ToPhoneNumber = x.ToPhoneNumber,
                Message = x.Message,
                Status = x.Status,
                SegmentCount = x.SegmentCount,
                UnitPriceDkk = x.UnitPriceDkk,
                TotalPriceDkk = x.TotalPriceDkk,
                IsBalanceDeducted = x.IsBalanceDeducted
            })
            .ToListAsync(cancellationToken);

        return Ok(new GetGlobalUsageReportCostResponseDto
        {
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            TotalCount = totalCount,
            TotalUsageDkk = totalUsageDkk,
            Items = items
        });
    }
}
