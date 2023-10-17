using Google.Protobuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CTraderManagerAPI.Exceptions
{
    public class ObserverException : Exception
    {
        public ObserverException(Exception innerException, IObserver<IMessage> observer) :
            base("An exception occurred while calling an observer OnNext method", innerException)
        {
            Observer = observer;
        }

        public IObserver<IMessage> Observer { get; }
    }
}
