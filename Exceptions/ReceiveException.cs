using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CTraderManagerAPI.Exceptions
{
    public class ReceiveException : Exception
    {
        public ReceiveException(Exception innerException) : base("An exception occurred while reading from stream", innerException)
        {
        }
    }
}
