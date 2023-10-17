using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CTraderManagerAPI.Exceptions
{
    public class ConnectionException : Exception
    {
        public ConnectionException(Exception innerException) : base("An exception occurred during OpenClient connection attempt", innerException)
        {
        }
    }
}
