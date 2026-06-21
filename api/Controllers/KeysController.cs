using System.Security.Cryptography;
using api.Auth;
using api.Data;
using api.Dtos.Errors;
using api.Dtos.Keys;
using api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
[Authorize(Policy = "MasterKeyOnly")]
public class KeysController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public KeysController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<GetAllKeysResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(UnauthorizedErrorDto), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ForbiddenErrorDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<GetAllKeysResponseDto>>> GetAll()
    {
        var keys = await _dbContext.ApiKeys
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new GetAllKeysResponseDto
            {
                Id = x.Id,
                Name = x.Name,
                IsActive = x.IsActive,
                Balance = x.Balance,
                ResponsibleName = x.ResponsibleName,
                ResponsibleEmail = x.ResponsibleEmail,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .ToListAsync();

        return Ok(keys);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CreateKeysResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(BadRequestErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ValidationErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ConflictErrorDto), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(UnauthorizedErrorDto), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ForbiddenErrorDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CreateKeysResponseDto>> Create([FromBody] CreateKeysRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new BadRequestErrorDto
            {
                Message = "Name is required.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        if (string.IsNullOrWhiteSpace(request.ResponsibleName))
        {
            return BadRequest(new BadRequestErrorDto
            {
                Message = "Responsible name is required.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        if (string.IsNullOrWhiteSpace(request.ResponsibleEmail))
        {
            return BadRequest(new BadRequestErrorDto
            {
                Message = "Responsible email is required.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        if (request.Balance < 0)
        {
            return BadRequest(new BadRequestErrorDto
            {
                Message = "Balance cannot be negative.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        var exists = await _dbContext.ApiKeys.AnyAsync(x => x.Name == request.Name);
        if (exists)
        {
            return Conflict(new ConflictErrorDto
            {
                Message = "An API key with this name already exists.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        var plainTextKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        var entity = new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            KeyHash = ApiKeyAuthenticationHandler.ComputeSha256(plainTextKey),
            IsActive = true,
            Balance = request.Balance,
            ResponsibleName = request.ResponsibleName.Trim(),
            ResponsibleEmail = request.ResponsibleEmail.Trim(),
            CreatedAt = DateTime.Now
        };

        _dbContext.ApiKeys.Add(entity);
        await _dbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, new CreateKeysResponseDto
        {
            Id = entity.Id,
            Name = entity.Name,
            IsActive = entity.IsActive,
            Balance = entity.Balance,
            ResponsibleName = entity.ResponsibleName,
            ResponsibleEmail = entity.ResponsibleEmail,
            CreatedAt = entity.CreatedAt,
            ApiKey = plainTextKey
        });
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(GetByIdKeysResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotFoundErrorDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(UnauthorizedErrorDto), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ForbiddenErrorDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GetByIdKeysResponseDto>> GetById([FromRoute] GetByIdKeysRequestDto request)
    {
        var key = await _dbContext.ApiKeys
            .AsNoTracking()
            .Where(x => x.Id == request.Id)
            .Select(x => new GetByIdKeysResponseDto
            {
                Id = x.Id,
                Name = x.Name,
                IsActive = x.IsActive,
                Balance = x.Balance,
                ResponsibleName = x.ResponsibleName,
                ResponsibleEmail = x.ResponsibleEmail,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .FirstOrDefaultAsync();

        if (key is null)
        {
            return NotFound(new NotFoundErrorDto
            {
                Message = "API key not found.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        return Ok(key);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(UpdateKeysResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(NotFoundErrorDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ConflictErrorDto), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(UnauthorizedErrorDto), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ForbiddenErrorDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<UpdateKeysResponseDto>> Update([FromRoute] Guid id, [FromBody] UpdateKeysRequestDto request)
    {
        request.Id = id;
        var key = await _dbContext.ApiKeys.FirstOrDefaultAsync(x => x.Id == id);
        if (key is null)
        {
            return NotFound(new NotFoundErrorDto
            {
                Message = "API key not found.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        if (!string.IsNullOrWhiteSpace(request.Name) && !string.Equals(key.Name, request.Name, StringComparison.Ordinal))
        {
            var nameExists = await _dbContext.ApiKeys.AnyAsync(x => x.Id != id && x.Name == request.Name);
            if (nameExists)
            {
                return Conflict(new ConflictErrorDto
                {
                    Message = "An API key with this name already exists.",
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            key.Name = request.Name.Trim();
        }

        if (request.IsActive.HasValue)
        {
            key.IsActive = request.IsActive.Value;
        }

        if (request.Balance.HasValue)
        {
            if (request.Balance.Value < 0)
            {
                return BadRequest(new BadRequestErrorDto
                {
                    Message = "Balance cannot be negative.",
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            key.Balance = request.Balance.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.ResponsibleName))
        {
            key.ResponsibleName = request.ResponsibleName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.ResponsibleEmail))
        {
            key.ResponsibleEmail = request.ResponsibleEmail.Trim();
        }

        key.UpdatedAt = DateTime.Now;
        await _dbContext.SaveChangesAsync();

        return Ok(new UpdateKeysResponseDto
        {
            Id = key.Id,
            Name = key.Name,
            IsActive = key.IsActive,
            Balance = key.Balance,
            ResponsibleName = key.ResponsibleName,
            ResponsibleEmail = key.ResponsibleEmail,
            CreatedAt = key.CreatedAt,
            UpdatedAt = key.UpdatedAt
        });
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(NotFoundErrorDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(UnauthorizedErrorDto), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ForbiddenErrorDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Delete([FromRoute] DeleteKeysRequestDto request)
    {
        var key = await _dbContext.ApiKeys.FirstOrDefaultAsync(x => x.Id == request.Id);
        if (key is null)
        {
            return NotFound(new NotFoundErrorDto
            {
                Message = "API key not found.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        _dbContext.ApiKeys.Remove(key);
        await _dbContext.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}")]
    [ProducesResponseType(typeof(RolloverKeysResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotFoundErrorDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(UnauthorizedErrorDto), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ForbiddenErrorDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RolloverKeysResponseDto>> Rollover([FromRoute] Guid id)
    {
        var key = await _dbContext.ApiKeys.FirstOrDefaultAsync(x => x.Id == id);
        if (key is null)
        {
            return NotFound(new NotFoundErrorDto
            {
                Message = "API key not found.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        var plainTextKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        key.KeyHash = ApiKeyAuthenticationHandler.ComputeSha256(plainTextKey);
        key.IsActive = true;
        key.UpdatedAt = DateTime.Now;

        await _dbContext.SaveChangesAsync();

        return Ok(new RolloverKeysResponseDto
        {
            Id = key.Id,
            Name = key.Name,
            IsActive = key.IsActive,
            Balance = key.Balance,
            ResponsibleName = key.ResponsibleName,
            ResponsibleEmail = key.ResponsibleEmail,
            CreatedAt = key.CreatedAt,
            UpdatedAt = key.UpdatedAt,
            ApiKey = plainTextKey
        });
    }
}
