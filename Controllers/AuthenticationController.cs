using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using CommandCentral.Framework;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace CommandCentral.Controllers
{
    [Route("api/[controller]")]
    public class AuthenticationController : CommandCentralController
    {
        [HttpPost("[action]")]
        [RequireAuthentication]
        public IActionResult Login([FromBody] DTOs.LoginRequestDTO dto)
        {
            return new OkObjectResult("test");
        }
    }
}
