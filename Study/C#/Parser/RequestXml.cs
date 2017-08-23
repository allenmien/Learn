using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Collections;

namespace iOubo.iSpider.Infrastructure.Parser
{

    public class RequestXml
    {
        XElement _rootXML;
        public RequestXml(string rootPath, string postfix)
        {
            string xmlFilePath = rootPath + "\\Parser\\XmlSetting\\Enterprise" + postfix + ".xml";
            this._rootXML = XElement.Load(xmlFilePath);
        }

        public IEnumerable<XElement> GetRequestListByGroup(string requestGroup)
        {
            IEnumerable<XElement> elements = from ele in _rootXML.Elements("request")
                                             where ele.Attribute("group").Value == requestGroup
                                             select ele;
            return elements;
        }
        public IEnumerable<XElement> GetRequestListByName(string requestName)
        {
            IEnumerable<XElement> elements = from ele in _rootXML.Elements("request")
                                             where ele.Attribute("name").Value == requestName
                                             select ele;
            return elements;
        }

        public XElement GetRequestItemByName(string requestName)
        {
            IEnumerable<XElement> elements = from ele in _rootXML.Elements("request")
                                             where ele.Attribute("name").Value == requestName
                                             select ele;
            return elements.First();
        }
        /// <summary>
        /// 根据名称删除节点
        /// </summary>
        /// <param name="requestName"></param>
        /// <returns></returns>
        public void RemoveNodeByName(string requestName)
        {
            var node = from ele in _rootXML.Elements("request")
                       where ele.Attribute("name").Value == requestName
                       select ele;
            node.Remove();
        }
    }
}
