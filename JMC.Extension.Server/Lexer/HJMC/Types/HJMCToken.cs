using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace JMC.Extension.Server.Lexer.HJMC.Types
{
    public class HJMCToken
    {
        public HJMCTokenType Type { get; set; }
#pragma warning disable CS8618 // Þo\®CsÂ× Null IÊK{ïÜñ Null ?B¿l¶é×Â× NullB
        public List<string> Values { get;set; }
#pragma warning restore CS8618 // Þo\®CsÂ× Null IÊK{ïÜñ Null ?B¿l¶é×Â× NullB
    }
}
