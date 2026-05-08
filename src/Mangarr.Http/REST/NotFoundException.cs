using System.Net;
using Mangarr.Http.Exceptions;

namespace Mangarr.Http.REST
{
    public class NotFoundException : ApiException
    {
        public NotFoundException(object content = null)
            : base(HttpStatusCode.NotFound, content)
        {
        }
    }
}
