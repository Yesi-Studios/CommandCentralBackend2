using FluentValidation;
using FluentValidation.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CommandCentral.DTOs
{
    [Validator(typeof(DTOValidator))]
    public class LoginRequestDTO
    {
        public string Username { get; set; }

        public string Password { get; set; }

        public class DTOValidator : AbstractValidator<LoginRequestDTO>
        {
            public DTOValidator()
            {
                RuleFor(x => x.Username).NotEmpty().MinimumLength(8);
                RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
            }
        }
    }
}
