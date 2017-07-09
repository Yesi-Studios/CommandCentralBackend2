using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Controllers;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.Abstractions;
using FluentValidation.Attributes;
using FluentValidation;
using FluentValidation.Results;
using CommandCentral.Models;

namespace CommandCentral.Framework
{
    public class CommandCentralController : Controller
    {

        public new Person User { get; set; }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            //Handle Authentication first

            if (context.ActionDescriptor.Parameters.Count > 1)
                throw new Exception("Why do we have more than one parameter?  HAVE WE LEARNED SOMETHING NEW?!");

            if (context.ActionDescriptor.Parameters.Count == 0)
                return;

            var parameter = context.ActionDescriptor.Parameters.First();
            var value = context.ActionArguments.First().Value;

            //First, if the model is required, let's check to see if its value is null.
            if (((ControllerParameterDescriptor)parameter).ParameterInfo.GetCustomAttribute<RequiredModelAttribute>() != null && value == null)
            {
                //So the value is null.  In this case, let's tell the client how to call this endpoint.
                context.ModelState.AddModelError(parameter.Name, "Please send the request properly.");
            }

            //If the model is not null, let's call the validator for the dto, if one exists.
            var validatorType = parameter.ParameterType.GetCustomAttribute<ValidatorAttribute>()?.ValidatorType;

            if (validatorType != null)
            {
                object validator = Activator.CreateInstance(validatorType);
                var result = (ValidationResult)validatorType.GetMethod("Validate", new[] { value.GetType() }).Invoke(validator, new[] { value });

                if (!result.IsValid)
                {
                    foreach (var error in result.Errors)
                    {
                        context.ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
                    }
                }
            }

            if (!context.ModelState.IsValid)
            {
                context.Result = BadRequest(context.ModelState.Values.Where(x => x.ValidationState == Microsoft.AspNetCore.Mvc.ModelBinding.ModelValidationState.Invalid)
                    .SelectMany(x => x.Errors.Select(y => y.ErrorMessage)).ToList());
            }

            base.OnActionExecuting(context);
        }

    }
}
