using api.Dtos.Health;
using api.Dtos.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace api.Controllers
{
    [ApiController]
    [Route("api/[controller]/[action]")]
    [AllowAnonymous]
    public class HealthController : ControllerBase
    {
        [HttpGet]
        [ProducesResponseType(typeof(GetHealthResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
        public ActionResult<GetHealthResponseDto> Get()
        {
            return Ok(new GetHealthResponseDto
            {
                Status = "Healthy",
                Timestamp = DateTime.Now
            });
        }
    }
}
