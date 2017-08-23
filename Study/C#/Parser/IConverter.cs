using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using iOubo.iSpider.Model;

namespace iOubo.iSpider.Infrastructure.Parser
{
    public interface IConverter
    {
        SummaryEntity ProcessRequestAndParse(RequestInfo requestInfo);
    }
}
