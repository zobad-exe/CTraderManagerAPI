using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CTraderManagerAPI.Model
{
    public class AccountsLite
    {
        public AccountsLite()
        {
                
        }

        public AccountsLite(long traderId, long login, string email, string firstName, string lastName)
        {
            TraderId = traderId;
            Login = login;
            Email = email;
            FirstName = firstName;
            LastName = lastName;
        }

        public long TraderId { get;set ; }
        public long Login { get;set ; }
        public string Email { get;set ; }
        public string FirstName { get;set ; }
        public string LastName { get;set ; }
    }
}
