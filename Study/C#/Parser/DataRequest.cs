using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Web;
using System.Threading;
using System.Configuration;

using iOubo.iSpider.Model;
using iOubo.iSpider.Common;
using iOubo.iSpider.Infrastructure.Parser;

namespace iOubo.iSpider.Infrastructure.Parser
{
    public class DataRequest
    {
        private RequestInfo _requestInfo;
        private string isParallelRequest = ConfigurationManager.AppSettings["IsParallelRequest"] == null ? "Y" : ConfigurationManager.AppSettings["IsParallelRequest"];
        private int maxParallelCount = int.Parse(ConfigurationManager.AppSettings["MaxParallelRequest"] == null ? "5" : ConfigurationManager.AppSettings["MaxParallelRequest"]);
        public DataRequest(RequestInfo requestInfo)
        {
            this._requestInfo = requestInfo;  
        }

        public List<ResponseInfo> GetResponseInfo(IEnumerable<XElement> elements)
        {
            //如果config 设置不并行请求
            if (isParallelRequest == "N")
                return GetResponseInfoOneTread(elements);
            
            //并行请求
            try
            {
                CancellationTokenSource cts = new CancellationTokenSource();//取消并行运算需要的类
                ParallelOptions pOption = new ParallelOptions()
                {
                    CancellationToken = cts.Token
                }; //并行运算选项
                pOption.MaxDegreeOfParallelism = maxParallelCount; //一个并行最多开启10条线程执行
                Task tasks = new Task(() =>
                {
                    Task.Factory.StartNew(() =>
                    {
                        Thread.Sleep(40000);
                        cts.Cancel();
                        Console.WriteLine("超过40s,取消并行运算");
                    });
                });

                List<ResponseInfo> responseList = new List<ResponseInfo>();
                List<Action> requests = new List<Action>();
                foreach (XElement el in elements)
                {
                    requests.Add(() => GetResponse(responseList, el));
                }

                System.Threading.Tasks.Parallel.Invoke(pOption, requests.ToArray()); //并行执行Task1和Task2

                return responseList;
            }
            catch (AggregateException ex)
            {
                // enumerate the exceptions that have been aggregated
                foreach (Exception inner in ex.InnerExceptions)
                {
                    LogHelper.Error("AggregateException InnerException:", inner);
                }

                throw;
            }
        }

        public List<ResponseInfo> GetResponseInfo(List<RequestSetting> requestList)
        {
            CancellationTokenSource cts = new CancellationTokenSource();//取消并行运算需要的类
            ParallelOptions pOption = new ParallelOptions()
            {
                CancellationToken = cts.Token
            }; //并行运算选项
            pOption.MaxDegreeOfParallelism = 15; //一个并行最多开启10条线程执行
            Task tasks = new Task(() =>
            {
                Task.Factory.StartNew(() =>
                {
                    Thread.Sleep(40000);
                    cts.Cancel();
                    Console.WriteLine("超过40s,取消并行运算");
                });
            });

            List<ResponseInfo> responseList = new List<ResponseInfo>();
            List<Action> requests = new List<Action>();
            foreach (RequestSetting el in requestList)
            {
                requests.Add(() => GetResponse(responseList, el));
            }

            System.Threading.Tasks.Parallel.Invoke(pOption, requests.ToArray()); //并行执行Task1和Task2

            return responseList;
        }

        private void GetResponse(List<ResponseInfo> responseList, XElement el)
        {

            int count = 0;
            while (true)
            {
                count++;
                var returnStr = RequestData(el);
                if (!returnStr.Data.Contains("onload=\"challenge();"))
                {
                    responseList.Add(returnStr);
                    break;
                }
                else
                {
                    if (count >= 3)
                    {
                        break;
                    }
                }
            }
            
        }

        private void GetResponse(List<ResponseInfo> responseList, RequestSetting el)
        {
            responseList.Add(RequestData(el));
        }
        public List<ResponseInfo> GetResponseInfoOneTread(IEnumerable<XElement> elements)
        {
            //
            List<ResponseInfo> responseList = new List<ResponseInfo>();
            foreach (XElement el in elements)
            {
                int count = 0;
                while (true)
                {
                    try
                    {
                        count++;
                        var returnStr = RequestData(el);
                        if (!returnStr.Data.Contains("onload=\"challenge();"))
                        {
                            responseList.Add(returnStr);
                            break;
                        }
                        else
                        {
                            if (count >= 3)
                            {
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        var url = el.Attributes().FirstOrDefault(p=>p.Name=="url").Value;
                        if (url.StartsWith("http://www.szcredit.com.cn/web/GSZJGSPT/QynbDetail.aspx"))//ZJ、GD_SZ
                        {
                            Console.WriteLine(ex);
                        }
                        else
                        {
                            throw ex;
                        }
                    }
                }

            }
            
            return responseList;
        }

        public ResponseInfo RequestData(RequestSetting el)
        {
            RequestHandler request = new RequestHandler();
            if (!String.IsNullOrEmpty(this._requestInfo.Referer)) request.Referer = this._requestInfo.Referer;
            if (!String.IsNullOrEmpty(this._requestInfo.ResponseEncoding)) request.ResponseEncoding = this._requestInfo.ResponseEncoding;
            if (this._requestInfo.Cookies != null) request.CookieContainer.Add(this._requestInfo.Cookies);
            if (this._requestInfo.Headers != null) request.Headers = this._requestInfo.Headers;
            if(this._requestInfo.Parameters.ContainsKey("wip"))request.Headers["X-Forwarded-For"] =this._requestInfo.Parameters["wip"];


            ResponseInfo responseInfo = new ResponseInfo(); 
            responseInfo.Name = el.Name;
            responseInfo.IsArray = el.IsArray;
            //LogHelper.Info("Request start - " + el.Name);
            try
            {
                switch (el.Method)
                {
                    case "post":
                        if (el.Url.Contains("zj.gsxt.gov.cn") && !string.IsNullOrEmpty(ConfigurationManager.AppSettings["abuproxyUser"]))
                        {
                            responseInfo.Data = request.HttpPostCoreZJ(el.Url, el.Data, false, _requestInfo.Province);
                        }
                        else
                        {
                            responseInfo.Data = request.HttpPost(el.Url, el.Data, false, _requestInfo.Province);
                        }
                        break;
                    default:
                        if (el.Url.Contains("zj.gsxt.gov.cn") && !string.IsNullOrEmpty(ConfigurationManager.AppSettings["abuproxyUser"]))
                        {
                            responseInfo.Data = request.HttpGetCoreZJ(el.Url, el.Data, _requestInfo.Province);
                        }
                        else
                        {
                            responseInfo.Data = request.HttpGet(el.Url, el.Data, _requestInfo.Province);
                        }
                        //responseInfo.LastCookieString = request.GetLastCookieString();
                        break;
                }
            }
            catch(Exception ex)
            {
                responseInfo.Data = string.Empty;
                LogHelper.Error(ex.Message + ex.StackTrace);
                if ((el.Url.Contains("zj.gsxt.gov.cn") && ex.Message == "浙江网站更换ip访问！"))
                {
                    throw ex;
                }
            }
            //LogHelper.Info("Request end - " + el.Name);

            return responseInfo;
        }
        public ResponseInfo RequestData(XElement requestElement, string respEncoding = "utf-8")
        {
            RequestHandler request = new RequestHandler();
            request.ResponseEncoding = respEncoding;
            if (!String.IsNullOrEmpty(this._requestInfo.Referer)) request.Referer = this._requestInfo.Referer;
            if (!String.IsNullOrEmpty(this._requestInfo.ResponseEncoding)) request.ResponseEncoding = this._requestInfo.ResponseEncoding;
            if (this._requestInfo.Cookies != null) request.CookieContainer.Add(this._requestInfo.Cookies);
            if (this._requestInfo.Headers != null) request.Headers = this._requestInfo.Headers;
            if (this._requestInfo.Parameters.ContainsKey("wip")) request.Headers["X-Forwarded-For"] = this._requestInfo.Parameters["wip"];

            string dataStr = "";
            IEnumerable<XElement> parameters = from param in requestElement.Elements("parameter")
                                               select param;
            foreach (XElement p in parameters)
            {
                dataStr = dataStr + p.Attribute("name").Value + "="
                    + HttpUtility.UrlEncode(SetRequestParamValue(p.Attribute("value").Value)) + "&";
            }


            if (dataStr.Length > 0) dataStr = dataStr.Substring(0, dataStr.Length - 1);

            ResponseInfo responseInfo = new ResponseInfo();
            responseInfo.Name = requestElement.Attribute("name").Value;
            responseInfo.IsArray = requestElement.Attribute("isArray").Value;
            //LogHelper.Info("Request start - " + requestElement.Attribute("name").Value);
            try
            {
                switch (requestElement.Attribute("method").Value)
                {
                    case "post":
                        if (requestElement.Attribute("url").Value.Contains("zj.gsxt.gov.cn") && !string.IsNullOrEmpty(ConfigurationManager.AppSettings["abuproxyUser"]))
                        {
                            responseInfo.Data = request.HttpPostCoreZJ(SetRequestParamValue(requestElement.Attribute("url").Value), dataStr, false, _requestInfo.Province);
                        }
                        else
                        {
                            responseInfo.Data = request.HttpPost(SetRequestParamValue(requestElement.Attribute("url").Value), dataStr, false, _requestInfo.Province);
                        }
                        break;
                    default:
                        if (requestElement.Attribute("url").Value.Contains("zj.gsxt.gov.cn") && !string.IsNullOrEmpty(ConfigurationManager.AppSettings["abuproxyUser"]))
                        {
                            responseInfo.Data = request.HttpGetCoreZJ(SetRequestParamValue(requestElement.Attribute("url").Value), dataStr, _requestInfo.Province);
                        }
                        else
                        {
                            responseInfo.Data = request.HttpGet(SetRequestParamValue(requestElement.Attribute("url").Value), dataStr, _requestInfo.Province);
                        }
                        responseInfo.LastCookieString = request.GetLastCookieString();
                        break;
                }
            }
            catch (Exception ex)
            {
                responseInfo.Data = string.Empty;
                LogHelper.Error(ex.Message + ex.StackTrace);
                if ((requestElement.Attribute("url").Value.Contains("zj.gsxt.gov.cn") && ex.Message == "浙江网站更换ip访问！"))
                {
                    Console.WriteLine("浙江网站更换ip访问.." + requestElement.Attribute("url").Value);
                }
            }
            //LogHelper.Info("Request end - " + requestElement.Attribute("name").Value);

            return responseInfo;
        }

        public RequestSetting GetRequestSetting(XElement requestElement,int seq_no=0)
        {
            string dataStr = "";
            IEnumerable<XElement> parameters = from param in requestElement.Elements("parameter")
                                               select param;
            foreach (XElement p in parameters)
            {
                dataStr = dataStr + p.Attribute("name").Value + "="
                    + HttpUtility.UrlEncode(SetRequestParamValue(p.Attribute("value").Value)) + "&";
            }

            if (dataStr.Length > 0) dataStr = dataStr.Substring(0, dataStr.Length - 1);

            RequestSetting setting = new RequestSetting();
            setting.Name = requestElement.Attribute("name").Value + "@"+seq_no;
            setting.IsArray = requestElement.Attribute("isArray").Value;
            setting.Method = requestElement.Attribute("method").Value;
            setting.Url = SetRequestParamValue(requestElement.Attribute("url").Value);
            setting.Data = dataStr;

            return setting;
        }

        public void AddRequestParameterIfNotExist(string key, string value)
        {
            if (!this._requestInfo.Parameters.ContainsKey(key))
                this._requestInfo.Parameters.Add(key, value);
        }
        public void AddOrUpdateRequestParameter(string key, string value)
        {
            if (!this._requestInfo.Parameters.ContainsKey(key))
                this._requestInfo.Parameters.Add(key, value);
            else
                this._requestInfo.Parameters[key] = value;
        }

        private string SetRequestParamValue(string value)
        {
            foreach (KeyValuePair<string, string> kvp in this._requestInfo.Parameters)
            {
                value = value.Replace("{" + kvp.Key + "}", kvp.Value);
            }

            return value;
        }
    }
}
