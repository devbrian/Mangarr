using System;
using Microsoft.AspNetCore.Mvc;

namespace Mangarr.Http.REST.Attributes
{
    [AttributeUsage(AttributeTargets.Method)]
    public class RestPostByIdAttribute : HttpPostAttribute
    {
    }
}
