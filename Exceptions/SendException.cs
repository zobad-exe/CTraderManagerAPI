using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CTraderManagerAPI.Exceptions
{
    public class SendException : Exception
    {
        public SendException(Exception innerException) : base("An exception occurred while writing on stream", innerException)
        {
        }
    }
}
