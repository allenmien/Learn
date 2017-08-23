using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

using iOubo.iSpider.Infrastructure.Parser;

namespace iOubo.iSpider.Infrastructure.Parser
{
    public class ConverterFactory
    {
        public IConverter CreateConverter(string province)
        {
            string converterName = "iOubo.iSpider.Infrastructure.Parser.Converter"+province;
            IConverter converter = (IConverter)Activator.CreateInstance(Type.GetType(converterName));

            return converter;
        }
    }
}
