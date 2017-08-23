using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Collections;
using System.Text.RegularExpressions;
using System.Web;

using Newtonsoft.Json;
using iOubo.iSpider.Model;
using HtmlAgilityPack;
using iOubo.iSpider.Common;
using System.Net;
using System.Configuration;
using OpenQA.Selenium.Chrome;
using System.Threading;
using System.IO;
using System.Drawing;
using MongoDB.Bson;

namespace iOubo.iSpider.Infrastructure.Parser
{
    public class ConverterGD : IConverter
    {
        DataRequest _request;
        RequestInfo _requestInfo;
        RequestXml _requestXml;
        EnterpriseInfo _enterpriseInfo = new EnterpriseInfo();
        List<AbnormalInfo> _abnormals = new List<AbnormalInfo>();
        List<CheckupInfo> _checkups = new List<CheckupInfo>();
        string IsLoadReportSZ = ConfigurationManager.AppSettings["IsLoadReportSZ"] == null ? "Y" : ConfigurationManager.AppSettings["IsLoadReportSZ"];
        string _isCloseGZ = ConfigurationManager.AppSettings.Get("IsCloseGZ");
        List<string> reportsNeedToLoad = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings.Get("ReportsNeedToLoad"))
? new List<string>() : ConfigurationManager.AppSettings.Get("ReportsNeedToLoad").Split(',').ToList();
        public string CookieString { get; set; }

        public RequestHandler request = new RequestHandler();
        /// <summary>
        /// 解析第一次页面获得的参数
        /// </summary>
        public Dictionary<string, string> parameters = new Dictionary<string, string>();
        string _enterpriseName = string.Empty;
        string _local = string.Empty;
        int _parallelCount = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings.Get("ParallelCount")) ? 10 : int.Parse(ConfigurationManager.AppSettings.Get("ParallelCount"));
        public SummaryEntity ProcessRequestAndParse(RequestInfo requestInfo)
        {
            if (requestInfo.Parameters.ContainsKey("name")) _enterpriseName = requestInfo.Parameters["name"];
            if ("Y".Equals(_isCloseGZ) && requestInfo.Parameters["host"] == "2")//屏蔽广州信息抓取
            {
                throw new Exception("Close access GuangZhou Websites....");
                //return new SummaryEntity()
                //{
                //    Enterprise = _enterpriseInfo,
                //    Abnormals = _abnormals,
                //    Checkups = _checkups
                //};
            }
            this._requestInfo = requestInfo;
            if (requestInfo.Parameters["host"] == "1")
            {
                requestInfo.ResponseEncoding = "GB2312";
            }
            this._request = new DataRequest(requestInfo);
            var province = string.Empty;
            if (requestInfo.Parameters["host"] == "0" || requestInfo.Parameters["host"] == "2")//广东:0、广州、2
            {

                province = requestInfo.Parameters["host"] == "0" ? "GD" : "GD_GZ";
                if (requestInfo.Parameters.ContainsKey("platform") && "LIST_API" == requestInfo.Parameters["platform"])
                {
                    this._requestXml = new RequestXml(requestInfo.CurrentPath, province + "_API");
                }
                else
                {
                    this._requestXml = new RequestXml(requestInfo.CurrentPath, province);
                }
                InitialEnterpriseInfo();

                List<ResponseInfo> responseList = _request.GetResponseInfo(_requestXml.GetRequestListByName("gongshang"));
                if (responseList != null && responseList.Any())
                {
                    HtmlDocument document = new HtmlDocument();
                    document.LoadHtml(responseList.First().Data);
                    var rootNode = document.DocumentNode;
                    this.LoadAndParseParameters(rootNode);
                    responseList.AddRange(_request.GetResponseInfo(_requestXml.GetRequestListByGroup("basic")));
                    if (requestInfo.Parameters.ContainsKey("platform") && "LIST_API" == requestInfo.Parameters["platform"])
                    {
                        if (this._requestInfo.Parameters.ContainsKey("platform"))
                        {
                            this._requestInfo.Parameters.Remove("platform");
                        }
                        _enterpriseInfo.parameters = this._requestInfo.Parameters;
                        this.LoadAndParseResponse_API(responseList.First());
                    }
                    else
                    {
                        Parallel.ForEach(responseList, new ParallelOptions { MaxDegreeOfParallelism = _parallelCount }, responseInfo => this.LoadAndParseResponse(responseInfo));
                    }

                }
            }
            else if (requestInfo.Parameters["host"] == "1")//深圳
            {
                
                province = "GD_SZ";
                if (requestInfo.Parameters.ContainsKey("platform") && "LIST_API" == requestInfo.Parameters["platform"])
                {
                    this._requestXml = new RequestXml(requestInfo.CurrentPath, province + "_API");
                }
                else
                {
                    this._requestXml = new RequestXml(requestInfo.CurrentPath, province);
                }
                InitialEnterpriseInfo();
                List<ResponseInfo> responseList = _request.GetResponseInfo(_requestXml.GetRequestListByName("gongshang"));
                if (responseList != null && responseList.Any())
                {
                    _requestInfo.Cookies = this.ExtractCookie(responseList.First().LastCookieString);
                    _requestInfo.Province = "GD_SZ";
                    HtmlDocument document = new HtmlDocument();
                    document.LoadHtml(responseList.First().Data);
                    var rootNode = document.DocumentNode;
                    if (requestInfo.Parameters.ContainsKey("platform") && "LIST_API" == requestInfo.Parameters["platform"])
                    {
                        if (this._requestInfo.Parameters.ContainsKey("platform"))
                        {
                            this._requestInfo.Parameters.Remove("platform");
                        }
                        _enterpriseInfo.parameters = this._requestInfo.Parameters;
                        this.LoadAndParseResponse_SZ_API(responseList.First());

                    }
                    else
                    {
                        Parallel.ForEach(responseList, new ParallelOptions { MaxDegreeOfParallelism = _parallelCount }, responseInfo => this.LoadAndParseResponse_SZ(responseInfo));
                    }
                    

                }
                
            }

            

            SummaryEntity summaryEntity = new SummaryEntity()
            {
                Enterprise = _enterpriseInfo,
                Abnormals = _abnormals,
                Checkups = _checkups
            };

            return summaryEntity;
        }
        private void InitialEnterpriseInfo()
        {
            _enterpriseInfo.province = this._requestInfo.Province;
            _enterpriseInfo.last_update_time = DateTime.Now;
            _enterpriseInfo.source = "Batch";
            
        }

        #region Cookie
        private CookieCollection ExtractCookie(string cookieStr)
        {
            cookieStr = cookieStr.Replace("path=/", "").Replace("HttpOnly", "").TrimEnd(new char[] { ';' });
            CookieCollection container = new CookieCollection();
            string[] cookies = cookieStr.Split(new char[]
			{
				';'
			});
            if (cookies != null)
            {
                string[] array = cookies;
                for (int i = 0; i < array.Length; i++)
                {
                    string cookie = array[i];
                    int index = cookie.IndexOf('=');
                    if (index > 0)
                    {
                        string key = cookie.Substring(0, index).Trim(new char[] { ',', ' ' });
                        string value = cookie.Substring(index + 1);
                        container.Add(new System.Net.Cookie(key, value, "/", "www.szcredit.org.cn"));
                    }
                }
            }
            return container;
        }
        #endregion

        #region CreateRequest
        DataRequest CreateRequest()
        {
            Dictionary<string, string> dic = new Dictionary<string, string>();
            RequestInfo rInfo = new RequestInfo()
            {

                Cookies = _requestInfo.Cookies,
                Headers = _requestInfo.Headers,
                Province = _requestInfo.Province,
                CurrentPath = _requestInfo.CurrentPath,

                Referer = _requestInfo.Referer,
                ResponseEncoding = _requestInfo.ResponseEncoding,

                RegNo = _requestInfo.RegNo
            };
            foreach (var kv in _requestInfo.Parameters)
            {
                if (!dic.ContainsKey(kv.Key))
                {
                    dic.Add(kv.Key, kv.Value);
                }

            }
            rInfo.Parameters = dic;
            DataRequest request = new DataRequest(rInfo);
            request = new DataRequest(rInfo);
            return request;
        }
        #endregion

        #region 获取请求参数--广州、广东
        /// <summary>
        /// 获取请求参数--广州
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseParameters(HtmlNode rootNode)
        {
            var entNo = rootNode.SelectSingleNode("//input[@id='entNo']");
            var entType = rootNode.SelectSingleNode("//input[@id='entType']");
            var regNo = rootNode.SelectSingleNode("//input[@id='regOrg']");
            var local = rootNode.SelectSingleNode("//input[@id='local']");
            if (entNo != null)
            {
                _requestInfo.Parameters.Add("entNo", entNo.Attributes["value"].Value);
                _requestInfo.Parameters.Add("entType", entType.Attributes["value"].Value);
                _requestInfo.Parameters.Add("regOrg", regNo.Attributes["value"].Value);

            }
            if (local != null)
            {
                _local = local.Attributes["value"].Value;
            }
        }
        #endregion

        #region 解析广东信息
        /// <summary>
        /// 解析广东信息
        /// </summary>
        /// <param name="responseData"></param>
        private void LoadAndParseResponse(ResponseInfo responseInfo)
        {
            if (string.IsNullOrWhiteSpace(responseInfo.Data)) return;
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseInfo.Data);
            HtmlNode rootNode = document.DocumentNode;
            HtmlNode.ElementsFlags.Remove("input");
            rootNode.OuterHtml.Replace("<br>", "");
            if (responseInfo.Name == "gongshang")
            {
                var contentDiv = rootNode.SelectSingleNode("//div[@class='mianBodyStyle']");
                if (contentDiv != null)
                {
                    Parallel.Invoke(new ParallelOptions { MaxDegreeOfParallelism = 5 },
                        () => this.LoadAndParseBasicInfo(contentDiv),
                        () => this.LoadAndParseEmployee(contentDiv),
                        () => this.LoadAndParseBranch(contentDiv),
                        () => this.LoadAndParseReport(contentDiv));
                    //this.LoadAndParseBasicInfo(contentDiv);
                    //this.LoadAndParseEmployee(contentDiv);
                    //this.LoadAndParseBranch(contentDiv);
                    //this.LoadAndParseReport(contentDiv);
                }

            }
            else if (responseInfo.Name == "partner")
            {
                this.LoadAndParsePartnerInfo(responseInfo.Data);
            }
            else if (responseInfo.Name == "changerecord")
            {
                this.LoadAndParseChangeRecord(responseInfo.Data);
            }
            else if (responseInfo.Name == "mortgage")
            {
                this.LoadAndParseMortgageInfo(responseInfo.Data);
            }
            else if (responseInfo.Name == "equity_quality")
            {
                this.LoadAndParseEquityQuality(responseInfo.Data);
            }
            else if (responseInfo.Name == "checkup")
            {
                this.LoadAndParseCheckups(responseInfo.Data);
            }
            else if (responseInfo.Name == "financial_contribution")
            {
                this.LoadAndParseFinancialContribution(responseInfo.Data);
            }
            else if (responseInfo.Name == "stockchange")
            {
                this.LoadAndParseStockChange(responseInfo.Data);
            }
            else if (responseInfo.Name == "knowledge_property")
            {
                this.LoadAndParseKnowledgeProperty(responseInfo.Data);
            }
            else if (responseInfo.Name == "licence")
            {
                this.LoadAndParseLicence(responseInfo.Data);
            }
            else if (responseInfo.Name == "administrative_punishment")
            {
                this.LoadAndParseAdministrativePunishment(responseInfo.Data);
            }
            else if (responseInfo.Name == "administrative_punishment_tw")
            {
                this.LoadAndParseAdministrativePunishment_TW(responseInfo.Data);
            }
            else if (responseInfo.Name == "abnormal")
            {
                this.LoadAndParseAbnormal(responseInfo.Data);
            }
            else if (responseInfo.Name == "licence_gs")
            {
                this.LoadAndParseLicense1(responseInfo.Data);
            }
            else if (responseInfo.Name == "judicial_freeze")
            {
                this.LoadAndParseJudicialFreeze(responseInfo.Data);
            }
        }

        #endregion

        #region 解析广东信息
        /// <summary>
        /// 解析广东信息
        /// </summary>
        /// <param name="responseData"></param>
        private void LoadAndParseResponse_API(ResponseInfo responseInfo)
        {
            if (string.IsNullOrWhiteSpace(responseInfo.Data)) return;
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseInfo.Data);
            HtmlNode rootNode = document.DocumentNode;
            HtmlNode.ElementsFlags.Remove("input");
            rootNode.OuterHtml.Replace("<br>", "");
            if (responseInfo.Name == "gongshang")
            {
                var contentDiv = rootNode.SelectSingleNode("//div[@class='mianBodyStyle']");
                if (contentDiv != null)
                {
                    this.LoadAndParseBasicInfo(contentDiv);

                }

            }
        }
        #endregion

        #region 解析广东基本信息
        /// <summary>
        /// 解析广东基本信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseBasicInfo(HtmlNode rootNode)
        {
            var table = rootNode.SelectSingleNode("//div[@class='infoStyle']/div/table");
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs == null || !trs.Any()) return;
                trs.Remove(0);
                //基本信息
                foreach (var tr in trs)
                {
                    var tds = tr.SelectNodes("./td");
                    if (tds == null && tr.InnerText.Contains("经营范围："))
                    {
                        tds = tr.SelectNodes("./tr/td");
                    }
                    if (tds == null) continue;
                    for (int i = 0; i < tds.Count; i++)
                    {
                        var title = tds[i].SelectSingleNode("./span[@class='label']").InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Replace("：", "");
                        var val = tds[i].SelectSingleNode("./span[@class='content']").InnerText.Replace("\r", "").Replace("\n", "").Replace(" ", "").Replace("\t", "")
                            .Replace("&nbsp;", "").Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                        switch (title)
                        {
                            case "注册号":
                            case "统一社会信用代码":
                            case "注册号/统一社会信用代码":
                            case "统一社会信用代码/注册号":
                                if (val.Length == 18)
                                    _enterpriseInfo.credit_no = val;
                                else
                                    _enterpriseInfo.reg_no = val;
                                break;
                            case "企业（机构）名称":
                            case "名称":
                            case "企业名称":
                                if (string.IsNullOrEmpty(_enterpriseInfo.name))
                                    _enterpriseInfo.name = val;
                                break;
                            case "类型":
                                _enterpriseInfo.econ_kind = val;
                                break;
                            case "法定代表人":
                            case "法人代表":
                            case "负责人":
                            case "股东":
                            case "经营者":
                            case "执行事务合伙人":
                            case "投资人":
                                _enterpriseInfo.oper_name = val;
                                break;
                            case "住所":
                            case "经营场所":
                            case "营业场所":
                            case "主要经营场所":
                                Address address = new Address();
                                address.name = "注册地址";
                                address.address = val;
                                address.postcode = "";
                                _enterpriseInfo.addresses.Add(address);
                                break;
                            case "注册资金":
                            case "注册资本":
                            case "成员出资总额":
                                _enterpriseInfo.regist_capi = val;
                                break;
                            case "成立日期":
                            case "登记日期":
                            case "注册日期":
                                _enterpriseInfo.start_date = val;
                                break;
                            case "营业期限自":
                            case "经营期限自":
                            case "合伙期限自":
                                _enterpriseInfo.term_start = val;
                                break;
                            case "营业期限至":
                            case "经营期限至":
                            case "合伙期限至":
                                _enterpriseInfo.term_end = val;
                                break;
                            case "经营范围":
                            case "业务范围":
                                _enterpriseInfo.scope = val;
                                break;
                            case "登记机关":
                                _enterpriseInfo.belong_org = val;
                                break;
                            case "核准日期":
                            case "发照日期":
                                _enterpriseInfo.check_date = val;
                                break;
                            case "登记状态":
                            case "经营状态":
                                _enterpriseInfo.status = val;
                                break;
                            case "吊销日期":
                            case "注销日期":
                                _enterpriseInfo.end_date = val;
                                break;
                            case "组成形式":
                                _enterpriseInfo.type_desc = val;
                                break;
                            default:
                                break;
                        }
                    }
                }

            }
        }
        #endregion

        #region 解析广东主要人员信息
        /// <summary>
        /// 解析广东主要人员信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseEmployee(HtmlNode rootNode)
        {
            var divs = rootNode.SelectNodes("./div[@class='infoBody']/div[@class='infoStyle']/input[@id='local']/div");
            if (divs == null)
            {
                divs = rootNode.SelectNodes("./div[@class='infoBody']/div[@class='infoStyle']/div");
            }
            if (divs != null && divs.Any())
            {
                foreach (var div in divs)
                {
                    if (div.InnerText.Contains("主要人员信息") || div.InnerText.Contains("参加经营的家庭成员姓名"))
                    {
                        var div_content = div.SelectSingleNode("./following-sibling::div[1]");
                        div_content.OuterHtml.Replace("<br>", "");
                        var trs = div_content.SelectNodes("./table/tr");
                        if (trs != null && trs.Any())
                        {
                            foreach (var tr in trs)
                            {
                                var tds = tr.SelectNodes("./td");
                                if (tds != null && tds.Any())
                                {
                                    foreach (var td in tds)
                                    {
                                        var span_name = td.SelectSingleNode("./div/div/span[@class='nameText']");
                                        var span_position = td.SelectSingleNode("./div/div/span[@class='positionText']");
                                        if (span_name != null)
                                        {
                                            Employee employee = new Employee();
                                            employee.seq_no = _enterpriseInfo.employees.Count + 1;
                                            employee.name = span_name.InnerText;
                                            if (span_position != null)
                                            {
                                                employee.job_title = span_position.InnerText;
                                            }

                                            _enterpriseInfo.employees.Add(employee);
                                        }
                                    }
                                }
                            }
                        }
                    }

                }
            }

        }
        #endregion

        #region 解析广东变更信息
        /// <summary>
        /// 解析广东变更信息
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseChangeRecord(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            var obj = document.Contains("obj") ? (document["obj"].IsBsonNull ? string.Empty : document["obj"].AsString) : string.Empty;
            var list = document.Contains("list") ? (document["list"].IsBsonNull ? null : document["list"] as BsonDocument) : null;
            var request = this.CreateRequest();
            if (list != null)
            {
                var arr = list["list"].IsBsonNull ? new BsonArray() : list["list"].AsBsonArray;
                foreach (var item in arr)
                {
                    ChangeRecord changeRecord = new ChangeRecord();
                    changeRecord.seq_no = _enterpriseInfo.changerecords.Count + 1;
                    changeRecord.change_item = item["altFiledName"].IsBsonNull ? string.Empty : item["altFiledName"].AsString;
                    changeRecord.before_content = item["altBe"].IsBsonNull ? string.Empty : item["altBe"].AsString;
                    changeRecord.after_content = item["altAf"].IsBsonNull ? string.Empty : item["altAf"].AsString;
                    changeRecord.change_date = item["altDate"].IsBsonNull ? string.Empty : item["altDate"].AsInt64.ToString();
                    changeRecord.change_date = this.ConvertStringToDate(changeRecord.change_date);
                    _enterpriseInfo.changerecords.Add(changeRecord);

                }
            }
        }
        #endregion

        #region 解析广东分支机构信息
        /// <summary>
        /// 解析广东分支机构信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseBranch(HtmlNode rootNode)
        {
            var div = rootNode.SelectSingleNode("./div[@class='infoBody']/div[@class='infoStyle']/input[@id='local']/div[@id='braFlag']");
            if (div != null)
            {
                var div_content = div.SelectSingleNode("./following-sibling::div[1]");
                div_content.OuterHtml.Replace("<br>", "");
                var trs = div_content.SelectNodes("./table/tr");
                if (trs != null && trs.Any())
                {
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Any())
                        {
                            foreach (var td in tds)
                            {
                                var span = td.SelectSingleNode("./div[@class='brabox']/div/span[@class='conpany']");
                                if (span != null)
                                {
                                    Branch branch = new Branch();
                                    branch.seq_no = _enterpriseInfo.branches.Count + 1;
                                    branch.name = span.InnerText;
                                    var span_regno = span.SelectSingleNode("./following-sibling::span[1]");
                                    branch.reg_no = span_regno == null ? string.Empty : span_regno.SelectSingleNode("./span").InnerText;
                                    var span_org = span.SelectSingleNode("./following-sibling::span[2]");
                                    branch.belong_org = span_org == null ? string.Empty : span_org.SelectSingleNode("./span").InnerText;
                                    if (string.IsNullOrWhiteSpace(branch.name) && string.IsNullOrWhiteSpace(branch.reg_no)) continue;
                                    _enterpriseInfo.branches.Add(branch);
                                }
                            }
                        }
                    }
                }
            }

        }
        #endregion

        #region 解析广东股东信息
        /// <summary>
        /// 解析广东股东信息
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParsePartnerInfo(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            int n;
            var obj = document.Contains("obj") ? (document["obj"].IsBsonNull ? string.Empty : document["obj"].AsString) : string.Empty;
            var list = document.Contains("list") ? (document["list"].IsBsonNull ? null : document["list"] as BsonDocument) : null;
            var request = this.CreateRequest();
            if (list != null)
            {
                var arr = list["list"].IsBsonNull ? new BsonArray() : list["list"].AsBsonArray;
                foreach (var item in arr)
                {
                    Partner partner = new Partner();
                    partner.seq_no = _enterpriseInfo.partners.Count + 1;
                    var name = item["inv"].IsBsonNull ? string.Empty : item["inv"].AsString;
                    partner.stock_name = name;
                    partner.stock_type = item["invType"].IsBsonNull ? string.Empty : item["invType"].AsString;
                    var certName = item["certName"].IsBsonNull ? string.Empty : item["certName"].AsString;
                    var certNo = item["certNo"].IsBsonNull ? string.Empty : item["certNo"].AsString;
                    partner.identify_type = string.IsNullOrWhiteSpace(certName) ? "（非公示项）" : certName;
                    partner.identify_no = partner.stock_type == "自然人股东" ? "（非公示项）" : certNo;

                    if (obj == "2")
                    {
                        var invNo = item["invNo"].IsBsonNull ? string.Empty : item["invNo"].AsString;
                        request.AddOrUpdateRequestParameter("invNo", invNo);
                        var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("partner_detail"));
                        if (responseList != null && responseList.Any())
                        {
                            this.LoadAndParsePartnerDetail(responseList.First().Data, partner);
                        }
                    }
                    _enterpriseInfo.partners.Add(partner);
                }
            }
        }
        #endregion

        #region 解析股东详情信息
        /// <summary>
        /// 解析股东详情信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="partner"></param>
        void LoadAndParsePartnerDetail(string responseData, Partner partner)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            var tables = rootNode.SelectNodes("//table[@class='tableInfo']");
            if (tables != null && tables.Any() && tables.Count == 3)
            {
                var shouldTable = tables[0];
                HtmlNodeCollection trList = shouldTable.SelectNodes("./tr");
                foreach (var tr in trList)
                {
                    var tdlist = tr.SelectNodes("./td");
                    if (tdlist[0].InnerText.Contains("认缴"))
                    {
                        partner.total_should_capi = tdlist[1].InnerText.Replace("\r\n", "").Trim();
                    }
                    else if (tdlist[0].InnerText.Contains("实缴"))
                    {
                        partner.total_real_capi = tdlist[1].InnerText.Replace("\r\n", "").Trim();
                    }
                }
                var should_table = tables[1];
                var real_table = tables[2];

                var should_trs = should_table.SelectNodes("./tr[@class='tablebodytext']");
                if (should_trs != null && should_trs.Any())
                {
                    foreach (var tr in should_trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        ShouldCapiItem sci = new ShouldCapiItem();
                        sci.invest_type = tds[0].InnerText;
                        sci.shoud_capi = tds[1].InnerText;
                        sci.should_capi_date = tds[2].InnerText;
                        partner.should_capi_items.Add(sci);
                    }

                }
                var real_trs = real_table.SelectNodes("./tr[@class='tablebodytext']");
                if (real_trs != null && real_trs.Any())
                {
                    foreach (var tr in real_trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        RealCapiItem rci = new RealCapiItem();
                        rci.invest_type = tds[0].InnerText;
                        rci.real_capi = tds[1].InnerText;
                        rci.real_capi_date = tds[2].InnerText;
                        partner.real_capi_items.Add(rci);
                    }
                }
            }
        }
        #endregion

        #region 广东动产抵押信息
        /// <summary>
        /// 广东动产抵押信息
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseMortgageInfo(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            var list = document.Contains("list") ? (document["list"].IsBsonNull ? null : document["list"] as BsonDocument) : null;
            var request = this.CreateRequest();
            if (list != null)
            {
                var arr = list["list"].IsBsonNull ? new BsonArray() : list["list"].AsBsonArray;
                
                foreach (var item in arr)
                {
                    List<RequestSetting> requests = new List<RequestSetting>();
                    MortgageInfo mortgage = new MortgageInfo();
                    mortgage.seq_no = _enterpriseInfo.mortgages.Count + 1;
                    mortgage.number = item["pleNo"].IsBsonNull ? string.Empty : item["pleNo"].AsString;
                    mortgage.date = item["regiDate"].IsBsonNull ? string.Empty : this.ConvertStringToDate(item["regiDate"].AsInt64.ToString());
                    mortgage.department = item["regOrgStr"].IsBsonNull ? string.Empty : item["regOrgStr"].AsString;
                    mortgage.public_date = item["pefPerForm"].IsBsonNull ? string.Empty : this.ConvertStringToDate(item["pefPerForm"].AsInt64.ToString());
                    if (!item["priClaSecAm"].IsBsonNull)
                    {

                        mortgage.amount = item["priClaSecAm"].BsonType == BsonType.Int32 ? item["priClaSecAm"].AsInt32.ToString() : item["priClaSecAm"].AsDouble.ToString();
                        mortgage.amount = string.IsNullOrWhiteSpace(mortgage.amount) ? string.Empty : mortgage.amount + "万元";
                    }

                    mortgage.status = item["type"].IsBsonNull ? string.Empty : item["type"].AsString;
                    if (_local == "nm")
                    {
                        mortgage.status = mortgage.status == "1" ? "有效" : "无效";
                        string url = string.Format("http://gd.gsxt.gov.cn/aiccips/GSpublicity/GSpublicityList.html?service=pleInfoData&pledgeid={0}&type={1}&entNo={2}&entType={3}&regOrg={4}",
                        item["pledgeid"].AsString,item["type"].AsString,_requestInfo.Parameters["entNo"],_requestInfo.Parameters["entType"],_requestInfo.Parameters["regOrg"]);
                        requests.Add(new RequestSetting() { Url = url, Method = "get", IsArray = "0" ,Name="mortgage_detail"});
                    }
                    else
                    {
                        if (mortgage.status != "4" && mortgage.status != "undefined")
                        {
                            mortgage.status = "有效";
                            string url = string.Format("http://gd.gsxt.gov.cn/aiccips/GSpublicity/GSpublicityList.html?service=pleInfoData&pleNo={0}&type={1}&entNo={2}&entType={3}&regOrg={4}",
                                mortgage.number,item["type"].AsString,_requestInfo.Parameters["entNo"],_requestInfo.Parameters["entType"],_requestInfo.Parameters["regOrg"]);
                            requests.Add(new RequestSetting() { Url = url, Method = "get", IsArray = "0", Name = "mortgage_detail" });
                        }
                        else
                        {
                            mortgage.status = "无效";
                        }
                    }
                    if (requests.Any())
                    {
                        var responseList = request.GetResponseInfo(requests);
                        if (responseList != null && responseList.Any())
                        {
                            this.LoadAndParseMortgageDetail(responseList.First().Data, mortgage);
                        }
                    }
                    _enterpriseInfo.mortgages.Add(mortgage);
                }
            }
        }
        #endregion

        #region 解析广东动产抵押详情信息
        /// <summary>
        /// 解析动产抵押详情信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="mortgage"></param>
        void LoadAndParseMortgageDetail(string responseData,MortgageInfo mortgage)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            var tables = rootNode.SelectNodes("//div/table[@class='tableInfo']");

            if(tables!=null && tables.Any() && tables.Count==4)
            {
                var tableDYQR = tables[1];
                var tableBDB = tables[2];
                var tableDYW = tables[3];
                if (tableDYQR != null)
                {
                    var trs = tableDYQR.SelectNodes("./tr");
                    if (trs != null && trs.Any())
                    {
                        trs.Remove(0);
                        foreach (var tr in trs)
                        {
                            var tds = tr.SelectNodes("./td");
                            if (tds != null && tds.Count == 4)
                            {
                                Mortgagee mortgagee = new Mortgagee();

                                mortgagee.seq_no = mortgage.mortgagees.Count + 1;
                                mortgagee.name = tds[1].InnerText;
                                mortgagee.identify_type = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "");
                                mortgagee.identify_no = tds[3].InnerText;
                                mortgage.mortgagees.Add(mortgagee);
                            }
                           
                        }
                    }
                }
                if (tableBDB != null)
                {
                    var trs = tableBDB.SelectNodes("./tr");
                    if (trs != null && trs.Any())
                    {
                        foreach (var tr in trs)
                        {
                            var tds = tr.SelectNodes("./td");
                            if (tds != null && tds.Any())
                            {
                                for (int i = 0; i < tds.Count; i+=2)
                                {
                                    switch (tds[i].InnerText)
                                    {
                                        case "种类":
                                            mortgage.debit_type = tds[i + 1].InnerText;
                                            break;
                                        case "数额":
                                            mortgage.debit_amount = tds[i + 1].InnerText;
                                            break;
                                        case "担保的范围":
                                            mortgage.debit_scope = tds[i + 1].InnerText;
                                            break;
                                        case "债务人履行债务的期限":
                                            mortgage.debit_period = tds[i + 1].InnerText;
                                            break;
                                        case "备注":
                                            mortgage.debit_remarks = tds[i + 1].InnerText;
                                            break;
                                        default:
                                            break;
                                    }
                                }
                            }
                        }
                    }
                }
                if (tableDYW != null)
                {
                    var trs = tableDYW.SelectNodes("./tr");
                    if (trs != null && trs.Any())
                    {
                        trs.Remove(0);
                        foreach (var tr in trs)
                        {
                            var tds = tr.SelectNodes("./td");
                            if (tds != null && tds.Count == 5)
                            {
                                Guarantee guarantee = new Guarantee();
                                guarantee.seq_no = mortgage.guarantees.Count + 1;
                                guarantee.name = tds[1].InnerText;
                                guarantee.belong_to = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "");
                                guarantee.desc = tds[3].InnerText;
                                guarantee.remarks = tds[4].InnerText;
                                mortgage.guarantees.Add(guarantee);
                            }
                           
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析广东股权出质信息
        /// <summary>
        /// 解析股权出质信息
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseEquityQuality(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            var list = document.Contains("list") ? (document["list"].IsBsonNull ? null : document["list"] as BsonDocument) : null;
            var request = this.CreateRequest();
            if (list != null)
            {
                var arr = list["list"].IsBsonNull ? new BsonArray() : list["list"].AsBsonArray;

                foreach (var item in arr)
                {
                    List<RequestSetting> requests = new List<RequestSetting>();
                    EquityQuality eq = new EquityQuality();
                    eq.seq_no = _enterpriseInfo.equity_qualities.Count + 1;
                    eq.number = item["stoRegNo"].IsBsonNull ? string.Empty : item["stoRegNo"].AsString.Replace(" ", "");
                    eq.pledgor = item["inv"].IsBsonNull ? string.Empty : item["inv"].AsString.Replace(" ", "");
                    eq.pledgor_identify_no = item["invType"].IsBsonNull ? string.Empty : item["invType"].AsString.Replace(" ", "");
                    eq.pledgor_identify_no = eq.pledgor_identify_no == "6" ? "居民身份证" : (item["invID"].IsBsonNull ? string.Empty : item["invID"].AsString);
                    eq.pledgor_amount = item["impAm"].IsBsonNull ? string.Empty :
                        (item["impAm"].BsonType == BsonType.Int32 ? item["impAm"].AsInt32.ToString().Replace(" ", "") : item["impAm"].AsDouble.ToString().Replace(" ", ""));
                    eq.pledgor_unit = item["pleAmUnit"].IsBsonNull ? string.Empty : item["pleAmUnit"].AsString.ToString().Replace(" ", "");
                    if (!string.IsNullOrWhiteSpace(eq.pledgor_amount))
                    {
                        eq.pledgor_amount = string.IsNullOrWhiteSpace(eq.pledgor_unit) ? "万元" : eq.pledgor_amount + eq.pledgor_unit;
                    }
                    eq.pawnee = item["impOrg"].IsBsonNull ? string.Empty : item["impOrg"].AsString.Replace(" ", "");
                    eq.pawnee_identify_no = item["impOrgType"].IsBsonNull ? string.Empty : item["impOrgType"].AsString.Replace(" ", "");
                    eq.pawnee_identify_no = eq.pawnee_identify_no == "4" ? "居民身份证" : (item["impOrgID"].IsBsonNull ? string.Empty : item["impOrgID"].AsString);
                    eq.date = item["registDate"].IsBsonNull ? string.Empty : this.ConvertStringToDate(item["registDate"].AsInt64.ToString());
                    eq.status = item["type"].IsBsonNull ? string.Empty : item["type"].AsString;
                    eq.public_date = eq.date;
                    eq.status = eq.status == "1" ? "有效" : "无效";
                    var domainUrl = _requestInfo.Parameters["host"] == "0" ? "http://gsxt.gdgs.gov.cn" : "http://gsxt.gzaic.gov.cn";
                    string url = string.Format("{4}/aiccips/GSpublicity/curStoPleXQ.html?stoPleNo={0}&type={1}&bizSeq={2}&regOrg={3}",
                        item["stoPleNo"].AsString, item["type"].AsString, item["bizSeq"].IsBsonNull ? string.Empty : item["bizSeq"].AsString, _requestInfo.Parameters["regOrg"], domainUrl);
                    requests.Add(new RequestSetting() { Url = url, Method = "get", IsArray = "0", Name = "equity_quality_detail" });

                    if (requests.Any())
                    {
                        var responseList = request.GetResponseInfo(requests);
                        if (responseList != null && responseList.Any())
                        {
                            this.LoadAndParseEquityQualityDetail(responseList.First().Data, eq);
                        }
                    }

                    _enterpriseInfo.equity_qualities.Add(eq);
                }
            }
        }
        #endregion

        #region 解析广东股权出质详情信息
        /// <summary>
        /// 解析股权出质详情信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="eq"></param>
        void LoadAndParseEquityQualityDetail(string responseData,EquityQuality eq)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            var table = rootNode.SelectSingleNode("//div/table[@class='tableInfo']");
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds=tr.SelectNodes("./td");
                        if (tds != null && tds.Count == 3)
                        {
                            ChangeItem ci = new ChangeItem();
                            ci.seq_no = eq.change_items.Count + 1;
                            ci.change_date = tds[1].InnerText;
                            ci.change_content = tds[2].InnerText;
                            eq.change_items.Add(ci);
                        }
                        
                    }
                }
            }
        }
        #endregion

        #region 解析广东抽查检查信息
        /// <summary>
        /// 解析广东抽查检查信息
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseCheckups(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            var list = document.Contains("list") ? (document["list"].IsBsonNull ? null : document["list"] as BsonDocument) : null;
            if (list != null)
            {
                var arr = list["list"].IsBsonNull ? new BsonArray() : list["list"].AsBsonArray;

                foreach (var item in arr)
                {
                    CheckupInfo checkup = new CheckupInfo();
                    checkup.name = _enterpriseInfo.name;
                    checkup.reg_no = _enterpriseInfo.reg_no;
                    checkup.province = _enterpriseInfo.province;
                    checkup.department = item["aicName"].IsBsonNull ? string.Empty : item["aicName"].AsString.Replace(" ", "");
                    checkup.type = item["typeStr"].IsBsonNull ? string.Empty : item["typeStr"].AsString.Replace(" ", "");
                    checkup.date = item["insDate"].IsBsonNull ? string.Empty : this.ConvertStringToDate(item["insDate"].AsInt64.ToString());
                    checkup.result = item["inspectDetail"].IsBsonNull ? string.Empty : item["inspectDetail"].AsString
                        .Replace(" ", "").Replace("1", "正常").Replace("2", "未按规定公示年报").Replace("3", "未按规定公示其他应当公示的信息").
                    Replace("4", "公示信息隐瞒真实情况、弄虚作假").Replace("5", "通过登记的住所（经营场所）无法联系").Replace("6", "不予配合情节严重").
                    Replace("7", "该主体已注销").Replace("8", "该主体未建财务账").Replace("9", "其他"); ;
                    _checkups.Add(checkup);
                }
            }
            
        }
        #endregion

        #region 解析广东股权冻结信息
        /// <summary>
        /// 解析广东股权冻结信息
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseJudicialFreeze(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            var list = document.Contains("list") ? (document["list"].IsBsonNull ? null : document["list"] as BsonDocument) : null;
            var request = this.CreateRequest();
            if (list != null)
            {
                var arr = list["list"].IsBsonNull ? new BsonArray() : list["list"].AsBsonArray;

                foreach (var item in arr)
                {
                    List<RequestSetting> requests = new List<RequestSetting>();
                    JudicialFreeze jf = new JudicialFreeze();
                    jf.seq_no = _enterpriseInfo.judicial_freezes.Count + 1;
                    if (_local == "nm")
                    {
                        jf.be_executed_person = item["inv"].IsBsonNull ? string.Empty : item["inv"].AsString;

                        jf.amount = this.GetAmount(item);
                        jf.executive_court = item["froAuth"].IsBsonNull ? string.Empty : item["froAuth"].AsString;
                        jf.number = item["executeNo"].IsBsonNull ? string.Empty : item["executeNo"].AsString;
                        jf.status = item["frozState"].IsBsonNull ? string.Empty : item["frozState"].AsString;
                        jf.status = jf.status == "1" ? "有效" : "无效";
                        string url = string.Format("http://gd.gsxt.gov.cn/aiccips/OtherPublicity/stockFrozenInfo.html?stoFroId={0}&entNo={1}",
                        item["stoFroId"].AsString, _requestInfo.Parameters["entNo"]);
                        requests.Add(new RequestSetting() { Url = url, Method = "get", IsArray = "0", Name = "judicial_freeze_detail" });
                    }
                    else
                    {
                        var entType = _requestInfo.Parameters["entType"];
                        var froAm = this.GetAmount(item);
                        var status = string.Empty;
                        if (!(entType == "9910" || entType == "9100" || entType == "9200"))
                        {
                            froAm = froAm + "万";
                        }

                        froAm = froAm + item["regCapCur"].AsString;
                        
                        if (_local == "hn")
                        {
                            if (item["frozState"].AsString == "1" || item["frozState"].AsString == "2" || item["frozState"].AsString == "3" || item["frozState"].AsString == "4")
                            {
                                status = "冻结";
                            }
                            else if (item["frozState"].AsString == "5")
                            {
                                status = "解除冻结";
                            }
                            else
                            {
                                status = "失效";
                            }
                        }
                        else
                        {
                            if (_requestInfo.Parameters["host"] == "2")
                            {
                                if (item["frozState"].AsString == "1")
                                {
                                    status = "冻结";
                                }
                                else if (item["frozState"].AsString == "2")
                                {
                                    status = "解除冻结";
                                }
                                else
                                {
                                    status = "失效";
                                }
                            }
                            else
                            {
                                if (item["frozState"].AsString == "1" || item["frozState"].AsString == "2" || item["frozState"].AsString == "3" || item["frozState"].AsString == "4")
                                {
                                    status = "冻结";
                                }
                                else if (item["frozState"].AsString == "5")
                                {
                                    status = "解除冻结";
                                }
                                else
                                {
                                    status = "失效";
                                }
                            }
                        }
                        jf.be_executed_person = item["inv"].IsBsonNull ? string.Empty : item["inv"].AsString;

                        jf.amount = froAm;
                        jf.executive_court = item["froAuth"].IsBsonNull ? string.Empty : item["froAuth"].AsString;
                        jf.number = item["executeNo"].IsBsonNull ? string.Empty : item["executeNo"].AsString;
                        jf.status = status;

                        string url = string.Format("http://gd.gsxt.gov.cn/aiccips/judiciaryAssist/shareholderFrozenInfo.html?regOrg={0}&sharFreeID={1}&entNo={2}",
                        _requestInfo.Parameters["regOrg"], item["sharFreeID"].AsString, _requestInfo.Parameters["entNo"]);
                        requests.Add(new RequestSetting() { Url = url, Method = "get", IsArray = "0", Name = "judicial_freeze_detail" });
                    }

                    if (requests.Any())
                    {
                        var responseList = request.GetResponseInfo(requests);
                        if (responseList != null && responseList.Any())
                        {
                            this.LoadAndParseJudicialFreezeDetail(responseList.First().Data, jf);
                            this.LoadAndParseUnFreezeDetail(responseList.First().Data, jf);
                        }
                    }
                    _enterpriseInfo.judicial_freezes.Add(jf);
                }
            }
        }
        #endregion

        #region 获取金额
        /// <summary>
        /// 获取金额
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        string GetAmount(BsonValue item)
        {
            string result = string.Empty;
            var froAm = item["froAm"];
            if(froAm.IsBsonNull)
            {
                result=string.Empty;
            }
            else if (froAm.BsonType == BsonType.Int32)
            {
                result = froAm.AsInt32.ToString();
            }
            else if (froAm.BsonType == BsonType.Int64)
            {
                result = froAm.AsInt64.ToString();
            }
            else if (froAm.BsonType == BsonType.Double)
            {
                result = froAm.AsDouble.ToString();
            }
            else
            {
                result = froAm.AsString;
            }
            return result;
        }
        #endregion

        #region 解析股权冻结详情信息
        /// <summary>
        /// 解析股权冻结详情信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="jf"></param>
        void LoadAndParseJudicialFreezeDetail(string responseData, JudicialFreeze jf)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            var tables = rootNode.SelectNodes("//div/table[@class='tableInfo']");
            if (tables != null && tables.Any())
            {
                var trList = tables.First().SelectNodes("./tr");
                if (trList != null && trList.Count > 1)
                {
                    JudicialFreezeDetail freeze = new JudicialFreezeDetail();
                    for (int i = 0; i < trList.Count; i++)
                    {
                        HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                        if (tdList != null && tdList.Any())
                        {
                            for (int j = 0; j < tdList.Count; j += 2)
                            {
                                var title = tdList[j].InnerText.Trim();
                                var val = tdList[j + 1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Trim();
                                switch (title)
                                {
                                    case "执行法院":
                                        freeze.execute_court = val;
                                        break;
                                    case "执行事项":
                                        freeze.assist_item = val;
                                        break;
                                    case "执行裁定书文号":
                                        freeze.adjudicate_no = val;
                                        break;
                                    case "执行通知书文号":
                                        freeze.notice_no = val;
                                        break;
                                    case "被执行人":
                                        freeze.assist_name = val;
                                        break;
                                    case "被执行人持有股份、其他投资权益的数额":
                                    case "被执行人持有股权、其它投资权益的数额":
                                        freeze.freeze_amount = val;
                                        break;
                                    case "被执行人证件种类":
                                    case "被执行人证照种类":
                                        freeze.assist_ident_type = val;
                                        break;
                                    case "被执行人证件号码":
                                    case "被执行人证照号码":
                                        freeze.assist_ident_no = val;
                                        break;
                                    case "冻结期限自":
                                        freeze.freeze_start_date = val;
                                        break;
                                    case "冻结期限至":
                                        freeze.freeze_end_date = val;
                                        break;
                                    case "冻结期限":
                                        freeze.freeze_year_month = val;
                                        break;
                                    case "公示日期":
                                        freeze.public_date = val;
                                        break;
                                }
                            }
                        }
                    }
                    jf.detail = freeze;
                }
            }
        }
        #endregion

        #region 解析股权冻结详情--解冻
        /// <summary>
        /// 解析股权冻结详情--解冻
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="jf"></param>
        void LoadAndParseUnFreezeDetail(string responseData, JudicialFreeze jf)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            var tables = rootNode.SelectNodes("//div/table[@class='tableInfo']");
            if (tables != null && tables.Count > 1)
            {
                JudicialUnFreezeDetail unfreeze = new JudicialUnFreezeDetail();
                var trList = tables[1].SelectNodes("./tr");
                for (int i = 0; i < trList.Count; i++)
                {
                    HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                    if (tdList != null && tdList.Any())
                    {
                        for (int j = 0; j < tdList.Count; j += 2)
                        {
                            var title = tdList[j].InnerText.Trim();
                            var val = tdList[j + 1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Trim();
                            switch (tdList[j].InnerText.Trim())
                            {
                                case "执行法院":
                                    unfreeze.execute_court = val;
                                    break;
                                case "执行事项":
                                    unfreeze.assist_item = val;
                                    break;
                                case "执行裁定书文号":
                                    unfreeze.adjudicate_no = val;
                                    break;
                                case "执行通知书文号":
                                    unfreeze.notice_no = val;
                                    break;
                                case "被执行人":
                                    unfreeze.assist_name = val;
                                    break;
                                case "被执行人持有股份、其他投资权益的数额":
                                case "被执行人持有股权、其它投资权益的数额":
                                    unfreeze.freeze_amount = val;
                                    break;
                                case "被执行人证件种类":
                                case "被执行人证照种类":
                                    unfreeze.assist_ident_type = val;
                                    break;
                                case "被执行人证件号码":
                                case "被执行人证照号码":
                                    unfreeze.assist_ident_no = val;
                                    break;
                                case "解除冻结日期":
                                case "解冻日期":
                                    unfreeze.unfreeze_date = val;
                                    break;
                                case "公示日期":
                                    unfreeze.public_date = val;
                                    break;
                            }
                        }
                    }
                }
                jf.un_freeze_detail = unfreeze;
            }
        }
        #endregion

        #region 解析广东股东及出资信息
        void LoadAndParseFinancialContribution(string responseData)
        {
            var request = this.CreateRequest();
            BsonDocument document = BsonDocument.Parse(responseData);
            var list = document.Contains("list") ? (document["list"].IsBsonNull ? null : document["list"] as BsonDocument) : null;
            if (list != null)
            {
                var pages = list["bottomPageNo"].IsBsonNull ? 1 : list["bottomPageNo"].AsInt32;
                var arr = list["selList"].IsBsonNull ? new BsonArray() : list["selList"].AsBsonArray;

                foreach (var item in arr)
                {
                    this.LoadAndParseFinancialContributionContent(item);
                }
                if (pages > 1)
                {
                    for (int i = 2; i <= pages; i++)
                    {
                        var requests = new List<RequestSetting>();
                        var domainUrl = _requestInfo.Parameters["host"] == "0" ? "http://gsxt.gdgs.gov.cn" : "http://gsxt.gzaic.gov.cn";
                        var url = string.Format("{3}/aiccips//REIInvInfo/REIInvInfoList?pageNo={0}&entNo={1}&regOrg={2}"
                            , i.ToString(), _requestInfo.Parameters["entNo"], _requestInfo.Parameters["regOrg"],domainUrl);
                        requests.Add(new RequestSetting() { Url = url, Method = "get", IsArray = "0", Name = "financial_contribution_page" });
                        var responseList = request.GetResponseInfo(requests);
                        if (responseList != null && responseList.Any())
                        {
                            BsonDocument inner_document = BsonDocument.Parse(responseList.First().Data);
                            var inner_list = inner_document.Contains("list") ? (inner_document["list"].IsBsonNull ? null : inner_document["list"] as BsonDocument) : null;
                            if (inner_list != null)
                            {
                                var inner_arr = inner_list["selList"].IsBsonNull ? new BsonArray() : inner_list["selList"].AsBsonArray;

                                foreach (var inner_item in inner_arr)
                                {
                                    if (inner_item.IsBsonNull) continue;
                                    this.LoadAndParseFinancialContributionContent(inner_item);
                                }
                            }
                        }
                    }
                }
            }
        }

        #endregion

        #region 解析广东股东及出资信息内容
        void LoadAndParseFinancialContributionContent(BsonValue item)
        {
            if (item.IsBsonNull) return;
            FinancialContribution fc = new FinancialContribution();
            fc.seq_no = _enterpriseInfo.financial_contributions.Count + 1;

            fc.investor_name = item["invName"].IsBsonNull ? string.Empty : item["invName"].AsString.Replace(" ", "");
            fc.total_should_capi = item["paidSumCount"].IsBsonNull ? string.Empty : (item["paidSumCount"].IsInt32 ? item["paidSumCount"].AsInt32 : item["paidSumCount"].AsDouble).ToString();
            fc.total_real_capi = item["acSumCount"].IsBsonNull ? string.Empty : (item["acSumCount"].IsInt32 ? item["acSumCount"].AsInt32 : item["acSumCount"].AsDouble).ToString();
            var inner_list = item["invFormList"].IsBsonNull ? null : item["invFormList"].AsBsonArray;
            if (inner_list != null && inner_list.Any())
            {
                foreach (var inner_item in inner_list)
                {
                    FinancialContribution.ShouldCapiItem sci = new FinancialContribution.ShouldCapiItem();
                    sci.should_invest_type = inner_item["paidForm"].IsBsonNull ? string.Empty : inner_item["paidForm"].AsString;
                    sci.should_capi = inner_item["paidAm"].IsBsonNull ? string.Empty : (inner_item["paidAm"].IsInt32 ? inner_item["paidAm"].AsInt32 : inner_item["paidAm"].AsDouble).ToString();
                    sci.should_invest_date = inner_item["paidDate"].IsBsonNull ? string.Empty : this.ConvertStringToDate(inner_item["paidDate"].AsInt64.ToString());
                    sci.public_date = string.Empty;
                    fc.should_capi_items.Add(sci);

                    FinancialContribution.RealCapiItem rci = new FinancialContribution.RealCapiItem();
                    rci.real_invest_type = inner_item["acForm"].IsBsonNull ? string.Empty : inner_item["acForm"].AsString;
                    rci.real_capi = inner_item["acAm"].IsBsonNull ? string.Empty : (inner_item["acAm"].IsInt32 ? inner_item["acAm"].AsInt32 : inner_item["acAm"].AsDouble).ToString();
                    rci.real_invest_date = inner_item["acDate"].IsBsonNull ? string.Empty : this.ConvertStringToDate(inner_item["acDate"].AsInt64.ToString());
                    rci.public_date = string.Empty;
                    fc.real_capi_items.Add(rci);
                }

            }
            _enterpriseInfo.financial_contributions.Add(fc);
        }
        #endregion

        #region 解析广东股权变更信息
        void LoadAndParseStockChange(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            var list = document.Contains("list") ? (document["list"].IsBsonNull ? null : document["list"] as BsonDocument) : null;
            if (list != null)
            {
                var arr = list["list"].IsBsonNull ? new BsonArray() : list["list"].AsBsonArray;

                foreach (var item in arr)
                {
                    StockChangeItem sci = new StockChangeItem();
                    sci.seq_no = _enterpriseInfo.stock_changes.Count + 1;
                    sci.name = item["guDName"].IsBsonNull ? string.Empty : item["guDName"].AsString;
                    sci.before_percent = item["transBePr"].IsBsonNull ? string.Empty : item["transBePr"].AsString;
                    sci.after_percent = item["transAfPr"].IsBsonNull ? string.Empty : item["transAfPr"].AsString;
                    sci.change_date = item["altDate"].IsBsonNull ? string.Empty : this.ConvertStringToDate(item["altDate"].AsInt64.ToString());
                    sci.public_date = sci.change_date;
                    _enterpriseInfo.stock_changes.Add(sci);
                }
            }
        }
        #endregion

        #region 解析广东知识产权出质登记信息
        /// <summary>
        /// 解析广东知识产权出质登记信息
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseKnowledgeProperty(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            var list = document.Contains("list") ? (document["list"].IsBsonNull ? null : document["list"] as BsonDocument) : null;
            if (list != null)
            {
                var arr = list["list"].IsBsonNull ? new BsonArray() : list["list"].AsBsonArray;

                foreach (var item in arr)
                {
                    KnowledgeProperty kp = new KnowledgeProperty();
                    kp.seq_no = _enterpriseInfo.knowledge_properties.Count + 1;
                    kp.number = item["intRegNo"].IsBsonNull ? string.Empty : item["intRegNo"].AsString;
                    kp.name = item["intName"].IsBsonNull ? string.Empty : item["intName"].AsString;
                    kp.type = item["types"].IsBsonNull ? string.Empty : item["types"].AsString;
                    kp.pledgor = item["invName"].IsBsonNull ? string.Empty : item["invName"].AsString;
                    kp.pawnee = item["pleName"].IsBsonNull ? string.Empty : item["pleName"].AsString;
                    kp.period = item["pleDate"].IsBsonNull ? string.Empty : this.ConvertStringToDate(item["pleDate"].AsInt64.ToString());
                    kp.status = item["registType"].IsBsonNull ? string.Empty : this.ConvertStringToDate(item["registType"].AsInt64.ToString());
                    kp.public_date = item["createDate"].IsBsonNull ? string.Empty : this.ConvertStringToDate(item["createDate"].AsInt64.ToString());
                    _enterpriseInfo.knowledge_properties.Add(kp);
                }
            }
        }
        #endregion

        #region 解析广东行政许可信息
        /// <summary>
        /// 解析广东行政许可信息
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseLicence(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            var list = document.Contains("list") ? (document["list"].IsBsonNull ? null : document["list"] as BsonDocument) : null;
            if (list != null)
            {
                var arr = list["list"].IsBsonNull ? new BsonArray() : list["list"].AsBsonArray;

                foreach (var item in arr)
                {
                    LicenseInfo licenceInfo = new LicenseInfo();
                    licenceInfo.seq_no = _enterpriseInfo.licenses.Count + 1;
                    licenceInfo.number = item["appPerCardID"].IsBsonNull ? string.Empty : item["appPerCardID"].AsString;
                    licenceInfo.name = item["appPerName"].IsBsonNull ? string.Empty : item["appPerName"].AsString;
                    licenceInfo.start_date = item["appPerStartTime"].IsBsonNull ? string.Empty : item["appPerStartTime"].AsString;
                    licenceInfo.end_date = item["appPerEndTime"].IsBsonNull ? "长期" : item["appPerEndTime"].AsString;
                    licenceInfo.department = item["appPerAuthory"].IsBsonNull ? string.Empty : item["appPerAuthory"].AsString;
                    licenceInfo.content = item["appPerContent"].IsBsonNull ? string.Empty : item["appPerContent"].AsString;
                    licenceInfo.status = item["appPerstatus"].IsBsonNull ? string.Empty : item["appPerstatus"].AsString;

                    _enterpriseInfo.licenses.Add(licenceInfo);
                }
            }
        }
        #endregion

        #region 解析行政许可 
        /// <summary>
        /// 解析行政许可
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseLicense1(string responseData)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            var divs = rootNode.SelectNodes("//div[@class='mianBodyStyle']/div[@class='infoBody']/div[@class='infoStyle']/div");
            if (divs != null && divs.Any())
            {
                var table = divs[1].SelectSingleNode("./table");
                if (table != null)
                {
                    var trs = table.SelectNodes("./tr[@class='tablebodytext']");
                    if (trs != null)
                    {
                        foreach (var tr in trs)
                        {
                            var tds = tr.SelectNodes("./td");
                            if (tds != null && tds.Count == 7)
                            {
                                LicenseInfo licenceInfo = new LicenseInfo();
                                licenceInfo.seq_no = _enterpriseInfo.licenses.Count + 1;
                                licenceInfo.number = tds[1].InnerText.Trim();
                                licenceInfo.name = tds[2].InnerText.Trim();
                                licenceInfo.start_date = tds[3].InnerText.Trim();
                                licenceInfo.end_date = tds[4].InnerText.Trim();
                                licenceInfo.department = tds[5].InnerText.Trim();
                                licenceInfo.content = tds[6].InnerText.Trim();
                                licenceInfo.status = string.Empty;

                                _enterpriseInfo.licenses.Add(licenceInfo);
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析广东行政处罚信息
        /// <summary>
        /// 解析广东行政许可信息
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseAdministrativePunishment(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            var list = document.Contains("list") ? (document["list"].IsBsonNull ? null : document["list"] as BsonDocument) : null;
            if (list != null)
            {
                var arr = list["list"].IsBsonNull ? new BsonArray() : list["list"].AsBsonArray;

                foreach (var item in arr)
                {
                    AdministrativePunishment ap = new AdministrativePunishment();
                    ap.seq_no = _enterpriseInfo.administrative_punishments.Count + 1;
                    ap.name = _enterpriseInfo.name;
                    ap.reg_no = _enterpriseInfo.reg_no;
                    ap.number = item["penDocNo"].IsBsonNull ? string.Empty : item["penDocNo"].AsString;
                    ap.illegal_type = item["illegalActType"].IsBsonNull ? string.Empty : item["illegalActType"].AsString;
                    ap.content = item["penalizeKind"].IsBsonNull ? string.Empty : item["penalizeKind"].AsString;
                    ap.department = item["penAuthory"].IsBsonNull ? string.Empty : item["penAuthory"].AsString;
                    ap.date = item["penDecisionTime"].IsBsonNull ? string.Empty : this.ConvertStringToDate(item["penDecisionTime"].AsInt64.ToString());
                    ap.remark = item["penComment"].IsBsonNull ? string.Empty : item["penComment"].AsString;
                    ap.date = item["createDate"].IsBsonNull ? string.Empty : this.ConvertStringToDate(item["createDate"].AsInt64.ToString());
                    _enterpriseInfo.administrative_punishments.Add(ap);
                }
            }
        }
        #endregion

        #region 解析行政处罚信息-TW
        /// <summary>
        /// 解析行政许可信息-TW
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseAdministrativePunishment_TW(string responseData)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            var divs = rootNode.SelectNodes("//div[@class='mianBodyStyle']/div[@class='infoBody']/div[@class='infoStyle']/div");
            if (divs != null && divs.Any())
            {
                var table = divs[1].SelectSingleNode("./table");
                if (table != null)
                {
                    var trs = table.SelectNodes("./tr[@class='tablebodytext']");
                    if (trs != null)
                    {
                        foreach (var tr in trs)
                        {
                            var tds = tr.SelectNodes("./td");
                            if (tds != null && tds.Count == 8)
                            {
                                AdministrativePunishment ap = new AdministrativePunishment();
                                ap.name = _enterpriseInfo.name;
                                ap.oper_name = _enterpriseInfo.oper_name;
                                ap.reg_no = string.IsNullOrWhiteSpace(_enterpriseInfo.reg_no) ? _enterpriseInfo.credit_no : _enterpriseInfo.reg_no;
                                ap.seq_no = _enterpriseInfo.administrative_punishments.Count + 1;
                                ap.number = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                ap.illegal_type = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                ap.content = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                ap.department = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                ap.date = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                ap.remark = string.Empty;
                                var aNode = tds[7].SelectSingleNode("./a");
                                if (aNode != null)
                                {
                                    var href = aNode.Attributes["href"].Value;

                                    var response = request.HttpGet(href, "", _enterpriseInfo.province);
                                    if (!string.IsNullOrWhiteSpace(response))
                                    {
                                        HtmlDocument inner_document = new HtmlDocument();
                                        inner_document.LoadHtml(response);
                                        var inner_rootNode = inner_document.DocumentNode;
                                        var inner_divs = inner_rootNode.SelectNodes("//div[@class='mianBodyStyle']/div[@class='infoBody']/div[@class='infoStyle']/div");
                                        if (inner_divs != null && inner_divs.Count == 4)
                                        {
                                            ap.description = inner_divs.Last().InnerHtml;
                                        }
                                    }

                                }
                                _enterpriseInfo.administrative_punishments.Add(ap);
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析广东经营异常
        /// <summary>
        /// 解析广东经营异常
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseAbnormal(string responseData)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            var table = rootNode.SelectSingleNode("//div[@class='infoStyle']/div/table[@id='paginList']");
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Count == 7)
                        {
                            AbnormalInfo abnormal = new AbnormalInfo();
                            abnormal.name = _enterpriseInfo.name;
                            abnormal.reg_no = _enterpriseInfo.reg_no;
                            abnormal.province = _enterpriseInfo.province;
                            abnormal.in_reason = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            abnormal.in_date = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            abnormal.department = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            abnormal.out_reason = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            abnormal.out_date = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            _abnormals.Add(abnormal);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析广东年报
        void LoadAndParseReport(HtmlNode rootNode)
        {
            
            var divs = rootNode.SelectNodes("./div[@class='infoBody']/div[@class='infoStyle']/input[@id='local']/div");
            
            if (divs != null && divs.Any())
            {
                foreach (var div in divs)
                {
                    if (div.InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "") == "企业年报信息"
                        || div.InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "") == "个体年报信息")
                    {
                        var table = div.SelectSingleNode("./following-sibling::div[1]/table[@class='tableInfo']");
                        if (table != null)
                        {
                            var trs = table.SelectNodes("./tr");
                            if (trs != null && trs.Any())
                            {
                                trs.Remove(0);
                                try
                                {
                                    Parallel.ForEach(trs, new ParallelOptions { MaxDegreeOfParallelism = _parallelCount }, tr => this.LoadAndParseReport_Parallel(tr));
                                }
                                catch
                                {
                                    _enterpriseInfo.reports.Clear();
                                }
                            }
                        }
                        break;
                    }
                }
            }
        }
        #endregion

        #region 解析年报信息--并行
        void LoadAndParseReport_Parallel(HtmlNode tr)
        {
            var request = this.CreateRequest();
            var tds = tr.SelectNodes("./td");
            if (tds != null && tds.Count == 4)
            {
                Report report = new Report();

                report.report_name = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                report.report_year = report.report_name.Substring(0, 4);
                report.report_date = tds[2].InnerText;
                var aNode = tds.Last().SelectSingleNode("./a");
                if (aNode != null)
                {
                    var onclick = aNode.Attributes.Contains("onclick") ? aNode.Attributes["onclick"].Value : string.Empty;
                    if (!string.IsNullOrWhiteSpace(onclick))
                    {
                        var arr = onclick.Split(new char[] { '(', ',', ')' });
                        request.AddOrUpdateRequestParameter("reportYear", arr[1]);
                        request.AddOrUpdateRequestParameter("entityType", arr[2]);
                        var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("report_detail"));
                        if (responseList != null && responseList.Any())
                        {
                            this.LoadAndParseReportDetail(responseList.First().Data, report);
                        }

                    }
                }
                _enterpriseInfo.reports.Add(report);
            }
        }
        #endregion

        #region 解析年报详情信息
        /// <summary>
        /// 解析年报详情信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        void LoadAndParseReportDetail(string responseData,Report report)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            var divs = rootNode.SelectNodes("//div[@class='marginleft']");
            if (divs != null && divs.Any())
            {
                foreach (var div in divs)
                {
                    var title = div.InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                    var table = div.SelectSingleNode("./following-sibling::div[1]/table");
                    if (title == "基本信息")
                    {
                        this.LoadAndParseReportBasicInfo(table, report);
                    }
                    else if (title == "网站或网店信息")
                    {
                        this.LoadAndParseReportWebsite(table, report);
                    }
                    else if (title == "对外投资信息")
                    {
                        this.LoadAndParseReportInvest(table, report);
                    }
                    else if (title.StartsWith("企业资产状况信息") || title.StartsWith("生产经营情况信息") || title.StartsWith("资产状况信息"))
                    {
                        this.LoadAndParseReportQYZC(table,report);
                    }
                    else if (title.StartsWith("股东及出资信息"))
                    {
                        this.LoadAndParseReportPartner(table, report);
                    }
                    else if (title.StartsWith("对外提供保证担保信息"))
                    {
                        this.LoadAndParseReportGuarantee(table, report);
                    }
                    else if (title.StartsWith("股权变更信息"))
                    {
                        this.LoadAndParseReportStockChange(table, report);
                    }
                    else if (title.StartsWith("修改信息"))
                    {
                        this.LoadAndParseUpdateRecord(table, report);
                    }
                    else if (title.StartsWith("社保信息"))
                    {
                        this.LoadAndParseSheBao(table, report);
                    }
                }
            }
        }
        #endregion

        #region 社保信息
        /// <summary>
        /// 社保信息
        /// </summary>
        /// <param name="table"></param>
        /// <param name="report"></param>
        void LoadAndParseSheBao(HtmlNode table, Report report)
        {

            HtmlNodeCollection trList = table.SelectNodes("./tr");

            foreach (HtmlNode rowNode in trList)
            {
                HtmlNodeCollection thList = rowNode.SelectNodes("./td[@class='tdTitleText']");
                HtmlNodeCollection tdList = rowNode.SelectNodes("./td[@class='baseText']");

                if (thList != null && tdList != null)
                {
                    if (thList.Count > tdList.Count)
                    {
                        thList.Remove(0);
                    }
                    for (int i = 0; i < thList.Count; i++)
                    {
                        switch (thList[i].InnerText.Trim())
                        {
                            case "城镇职工基本养老保险":
                                report.social_security.yanglaobx_num = tdList[i].InnerText.Trim();
                                break;
                            case "失业保险":
                                report.social_security.shiyebx_num = tdList[i].InnerText.Trim();
                                break;
                            case "职工基本医疗保险":
                                report.social_security.yiliaobx_num = tdList[i].InnerText.Trim();
                                break;
                            case "工伤保险":
                                report.social_security.gongshangbx_num = tdList[i].InnerText.Trim();
                                break;
                            case "生育保险":
                                report.social_security.shengyubx_num = tdList[i].InnerText.Trim();
                                break;
                            case "单位参加城镇职工基本养老保险缴费基数":
                                report.social_security.dw_yanglaobx_js = tdList[i].InnerText.Trim();
                                break;
                            case "单位参加失业保险缴费基数":
                                report.social_security.dw_shiyebx_js = tdList[i].InnerText.Trim();
                                break;
                            case "单位参加职工基本医疗保险缴费基数":
                                report.social_security.dw_yiliaobx_js = tdList[i].InnerText.Trim();
                                break;
                            case "单位参加生育保险缴费基数":
                                report.social_security.dw_shengyubx_js = tdList[i].InnerText.Trim();
                                break;
                            case "参加城镇职工基本养老保险本期实际缴费金额":
                                report.social_security.bq_yanglaobx_je = tdList[i].InnerText.Trim();
                                break;
                            case "参加失业保险本期实际缴费金额":
                                report.social_security.bq_shiyebx_je = tdList[i].InnerText.Trim();
                                break;
                            case "参加职工基本医疗保险本期实际缴费金额":
                                report.social_security.bq_yiliaobx_je = tdList[i].InnerText.Trim();
                                break;
                            case "参加工伤保险本期实际缴费金额":
                                report.social_security.bq_gongshangbx_je = tdList[i].InnerText.Trim();
                                break;
                            case "参加生育保险本期实际缴费金额":
                                report.social_security.bq_shengyubx_je = tdList[i].InnerText.Trim();
                                break;
                            case "单位参加城镇职工基本养老保险累计欠缴金额":
                                report.social_security.dw_yanglaobx_je = tdList[i].InnerText.Trim();
                                break;
                            case "单位参加失业保险累计欠缴金额":
                                report.social_security.dw_shiyebx_je = tdList[i].InnerText.Trim();
                                break;
                            case "单位参加职工基本医疗保险累计欠缴金额":
                                report.social_security.dw_yiliaobx_je = tdList[i].InnerText.Trim();
                                break;
                            case "单位参加工伤保险累计欠缴金额":
                                report.social_security.dw_gongshangbx_je = tdList[i].InnerText.Trim();
                                break;
                            case "单位参加生育保险累计欠缴金额)":
                            case "单位参加生育保险累计欠缴金额":
                                report.social_security.dw_shengyubx_je = tdList[i].InnerText.Trim();
                                break;
                            default:
                                break;
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析广东年报--基本信息
        /// <summary>
        /// 解析广东年报--基本信息
        /// </summary>
        /// <param name="table"></param>
        /// <param name="report"></param>
        void LoadAndParseReportBasicInfo(HtmlNode table,Report report)
        {
            if (table == null) return;
            var trs = table.SelectNodes("./tr");
            if (trs != null && trs.Any())
            {
                foreach (var tr in trs)
                {
                    var tds = tr.SelectNodes("./td");
                    if (tds != null && tds.Any())
                    {
                        foreach (var td in tds)
                        {
                            var spans = td.SelectNodes("./span");
                            if (spans != null && spans.Count == 3)
                            {
                                var title = spans[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").TrimEnd(new char[] { '：' });
                                var val = spans.Last().InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Replace("&nbsp;", "").Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                                switch (title)
                                {
                                    case "注册号":
                                    case "营业执照注册号":
                                        report.reg_no = val;
                                        break;
                                    case "统一社会信用代码":
                                        report.credit_no = val;
                                        break;
                                    case "注册号/统一社会信用代码":
                                    case "统一社会信用代码/注册号":
                                        if (val.Length == 18)
                                            report.credit_no = val;
                                        else
                                            report.reg_no = val;
                                        break;
                                    case "企业名称":
                                    case "名称":
                                    case "经营者":
                                        report.name = val;
                                        break;
                                    case "企业联系电话":
                                    case "联系电话":
                                        report.telephone = val;
                                        break;
                                    case "企业通信地址":
                                        report.address = val;
                                        break;
                                    case "邮政编码":
                                        report.zip_code = val;
                                        break;
                                    case "企业电子邮箱":
                                    case "电子邮箱":
                                        report.email = val;
                                        break;
                                    case "企业是否有投资信息或购买其他公司股权":
                                    case "企业是否有对外投资设立企业信息":
                                    case "是否有投资信息或购买其他公司股权":
                                        report.if_invest = val;
                                        break;
                                    case "是否有网站或网店":
                                    case "是否有网站或网点":
                                        report.if_website = val;
                                        break;
                                    case "企业经营状态":
                                        report.status = val;
                                        break;
                                    case "从业人数":
                                        report.collegues_num = val;
                                        break;
                                    case "有限责任公司本年度是否发生股东股权转让":
                                        report.if_equity = val;
                                        break;
                                    case "是否有对外提供担保信息":
                                        report.if_external_guarantee = val;
                                        break;
                                    default:
                                        break;
                                }
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析广东网站--年报
        /// <summary>
        /// 解析广东年报--网站
        /// </summary>
        /// <param name="table"></param>
        /// <param name="report"></param>
        void LoadAndParseReportWebsite(HtmlNode table,Report report)
        {
            if (table == null) return;
            var tds = table.SelectNodes("./tr/td");
            if (tds != null && tds.Any())
            {
                foreach (var td in tds)
                {
                    var divs = td.SelectNodes("./div[@class='webBox']/div[@class='webInfo']/div");
                    if (divs != null && divs.Count == 3)
                    {
                        WebsiteItem website = new WebsiteItem();
                        website.seq_no = report.websites.Count + 1;
                        website.web_name = divs[0].InnerText;
                        website.web_type = divs[1].InnerText.Replace("·", "").Replace("类型：", "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        website.web_url = divs[2].InnerText.Replace(".", "").Replace("·", "").Replace("网址：", "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        report.websites.Add(website);
                    }
                    
                }
            }
        }
        #endregion

        #region 解析广东年报对外投资--年报
        /// <summary>
        /// 解析广东年报对外投资--年报
        /// </summary>
        /// <param name="table"></param>
        /// <param name="report"></param>
        void LoadAndParseReportInvest(HtmlNode table, Report report)
        {
            if (table == null) return;
            var tds = table.SelectNodes("./tr/td");
            if (tds != null && tds.Any())
            {
                foreach (var td in tds)
                {
                    var divs = td.SelectNodes("./div[@class='investmentBox']/div[@class='webInfo']/div");
                    if (divs != null && divs.Count == 2)
                    {
                        InvestItem invest = new InvestItem();
                        invest.seq_no = report.invest_items.Count + 1;
                        invest.invest_name = divs[0].InnerText;
                        invest.invest_reg_no = divs[1].InnerText.Replace("· 统一社会信用代码/注册号：", "");
                        report.invest_items.Add(invest);
                    }

                }
            }
        }
        #endregion

        #region 解析股东及出资信息
        /// <summary>
        /// 解析股东及出资信息
        /// </summary>
        /// <param name="table"></param>
        /// <param name="report"></param>
        void LoadAndParseReportPartner(HtmlNode table, Report report)
        {
            if (table == null) return;
            var trs = table.SelectNodes("./tr[@class='tablebodytext']");
            if (trs != null && trs.Any())
            {
                foreach (var tr in trs)
                {
                    var tds = tr.SelectNodes("./td");
                    if (tds != null && tds.Any())
                    {
                        Partner partner = new Partner();
                        partner.seq_no = report.partners.Count + 1;
                        partner.stock_name = tds[1].InnerText;
                        ShouldCapiItem sci = new ShouldCapiItem();
                        sci.shoud_capi = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        sci.should_capi_date = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        sci.invest_type = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        partner.should_capi_items.Add(sci);

                        RealCapiItem rci = new RealCapiItem();
                        rci.real_capi = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        rci.real_capi_date = tds[6].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        rci.invest_type = tds[7].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        partner.real_capi_items.Add(rci);
                        
                        report.partners.Add(partner);
                    }
                }
                if (report.partners.Count >= 5)
                {
                    this.LoadAndParseReportPartnerPage(report);
                }
            }
        }
    
        #endregion

        #region 解析广东年报股东分页
        /// <summary>
        /// 解析广东年报股东分页
        /// </summary>
        /// <param name="report"></param>
        void LoadAndParseReportPartnerPage(Report report)
        {
            int i = 2;
            var request = this.CreateRequest();
            while (true)
            {
                request.AddOrUpdateRequestParameter("pageNo",i.ToString());
                request.AddOrUpdateRequestParameter("reportYear", report.report_year.ToString());
                var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("report_partner"));
                if (responseList != null && responseList.Any())
                {
                    var document = BsonDocument.Parse(responseList.First().Data);
                    if (document != null && document.Contains("list") && !document["list"].IsBsonNull)
                    {
                        var arr = document["list"].AsBsonArray;
                        if (arr != null && arr.Any())
                        {
                            foreach (var item in arr)
                            {
                                Partner partner = new Partner();
                                partner.seq_no = report.partners.Count + 1;
                                partner.stock_name = item["userName"].IsBsonNull ? string.Empty : item["userName"].AsString;
                                ShouldCapiItem sci = new ShouldCapiItem();
                                sci.shoud_capi = item["capShould"].IsBsonNull ? string.Empty
                                    : (item["capShould"].IsInt32 ? item["capShould"].AsInt32.ToString() : item["capShould"].AsDouble.ToString()) + "万人民币元";
                                sci.should_capi_date = item["capShouldDate"].IsBsonNull ? string.Empty : this.ConvertStringToDate(item["capShouldDate"].AsInt64.ToString());
                                sci.invest_type = item["capShouldType"].IsBsonNull ? string.Empty : item["capShouldType"].AsString
                                    .Replace("C", "货币").Replace("P", "实物").Replace("I", "知识产权").Replace("D", "债权").Replace("L", "土地使用权").Replace("S", "股权").Replace("O", "其他");
                                partner.should_capi_items.Add(sci);

                                RealCapiItem rci = new RealCapiItem();
                                rci.real_capi = item["capReal"].IsBsonNull ? string.Empty
                                    : (item["capReal"].IsInt32 ? item["capReal"].AsInt32.ToString() : item["capReal"].AsDouble.ToString()) + "万人民币元";
                                rci.real_capi_date = item["capDate"].IsBsonNull ? string.Empty : this.ConvertStringToDate(item["capDate"].AsInt64.ToString());
                                rci.invest_type = item["capType"].IsBsonNull ? string.Empty : item["capType"].AsString
                                    .Replace("C", "货币").Replace("P", "实物").Replace("I", "知识产权").Replace("D", "债权").Replace("L", "土地使用权").Replace("S", "股权").Replace("O", "其他");
                                partner.real_capi_items.Add(rci);
                                report.partners.Add(partner);
                                
                            }
                            if (arr.Count >= 5)
                            {
                                i++;
                            }
                            else
                            {
                                break;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
        #endregion

        #region 股权变更信息--年报
        /// <summary>
        /// 股权变更信息--年报
        /// </summary>
        /// <param name="table"></param>
        /// <param name="report"></param>
        void LoadAndParseReportStockChange(HtmlNode table,Report report)
        {
            if (table == null) return;
            var trs = table.SelectNodes("./tr");
            if (trs != null && trs.Any())
            {
                trs.Remove(0);
                foreach (var tr in trs)
                {
                    var tds = tr.SelectNodes("./td");
                    if (tds != null && tds.Any() && tds.Count == 5)
                    {
                        StockChangeItem sci = new StockChangeItem();
                        sci.seq_no = report.stock_changes.Count + 1;
                        sci.name = tds[1].InnerText;
                        sci.before_percent = tds[2].InnerText;
                        sci.after_percent = tds[3].InnerText;
                        sci.change_date = tds[4].InnerText;
                        report.stock_changes.Add(sci);
                    }
                }
                if (report.partners.Count >= 5)
                {
                    this.LoadAndParseReportStockChangePage(report);
                }
            }
        }
        #endregion

        #region 解析广东股权变更--年报
        /// <summary>
        /// 解析广东股权变更--年报
        /// </summary>
        /// <param name="report"></param>
        void LoadAndParseReportStockChangePage(Report report)
        {
            int i = 2;
            var request = this.CreateRequest();
            while (true)
            {
                request.AddOrUpdateRequestParameter("pageNo", i.ToString());
                request.AddOrUpdateRequestParameter("reportYear", report.report_year.ToString());
                var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("report_stockchange"));
                if (responseList != null && responseList.Any())
                {
                    if (string.IsNullOrWhiteSpace(responseList.First().Data)) return;
                    var document = BsonDocument.Parse(responseList.First().Data);
                    if (document != null && document.Contains("list") && !document["list"].IsBsonNull)
                    {
                        var arr = document["list"].AsBsonArray;
                        if (arr != null && arr.Any())
                        {
                            foreach (var item in arr)
                            {
                                StockChangeItem sci = new StockChangeItem();
                                sci.seq_no = report.stock_changes.Count + 1;
                                sci.name = item["inv"].IsBsonNull ? string.Empty : item["inv"].AsString;
                                sci.before_percent = item["transAmPr"].IsBsonNull ? string.Empty : item["transAmPr"].AsString + "%";
                                sci.after_percent = item["amPr"].IsBsonNull ? string.Empty : item["amPr"].AsString + "%";
                                sci.change_date = item["transferDate"].IsBsonNull ? string.Empty : item["transferDate"].AsInt64.ToString();
                                report.stock_changes.Add(sci);
                            }
                            if (arr.Count >= 5)
                            {
                                i++;
                            }
                            else
                            {
                                break;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
        #endregion

        #region 解析广东修改信息--年报
        /// <summary>
        /// 解析广东修改信息--年报
        /// </summary>
        /// <param name="table"></param>
        /// <param name="report"></param>
        void LoadAndParseUpdateRecord(HtmlNode table, Report report)
        {
            if (table == null) return;
            var trs = table.SelectNodes("./tr");
            if (trs != null && trs.Any())
            {
                trs.Remove(0);
                foreach (var tr in trs)
                {
                    var tds = tr.SelectNodes("./td");
                    if (tds != null && tds.Any() && tds.Count == 5)
                    {
                        UpdateRecord ur = new UpdateRecord();
                        ur.seq_no = report.update_records.Count + 1;
                        ur.update_item = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        ur.before_update = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        ur.after_update = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        ur.update_date = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        report.update_records.Add(ur);
                    }
                }
                if (report.update_records.Count >= 5)
                {
                    this.LoadAndParseReportStockChangePage(report);
                }
            }
        }
        #endregion

        #region 解析广东对外提供保证担保信息--年报
        /// <summary>
        /// 解析广东对外提供保证担保信息--年报
        /// </summary>
        /// <param name="table"></param>
        /// <param name="report"></param>
        void LoadAndParseReportGuarantee(HtmlNode table, Report report)
        {
            if (table == null) return;
            var trs = table.SelectNodes("./tr");
            if (trs != null && trs.Any())
            {
                trs.Remove(0);
                foreach (var tr in trs)
                {
                    var tds = tr.SelectNodes("./td");
                    if (tds != null && tds.Any() && tds.Count == 8)
                    {
                        ExternalGuarantee eg = new ExternalGuarantee();
                        eg.seq_no = report.external_guarantees.Count + 1;
                        eg.creditor = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        eg.debtor = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        eg.type = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        eg.amount = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        eg.period = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        eg.guarantee_time = tds[6].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        eg.guarantee_type = tds[7].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        report.external_guarantees.Add(eg);
                    }
                }
                if (report.external_guarantees.Count >= 5)
                {
                    //this.LoadAndParseReportStockChangePage(report);
                }
            }
        }
        #endregion

        #region 解析广东对外提供保证担保信息分页--年报
        void LoadAndParseReportGuaranteePage(Report report)
        {
            int i = 2;
            var request = this.CreateRequest();
            while (true)
            {
                request.AddOrUpdateRequestParameter("pageNo", i.ToString());
                request.AddOrUpdateRequestParameter("reportYear", report.report_year.ToString());
                var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("report_external_guarantee"));
                if (responseList != null && responseList.Any())
                {
                    if (string.IsNullOrWhiteSpace(responseList.First().Data)) return;
                    var document = BsonDocument.Parse(responseList.First().Data);
                    if (document != null && document.Contains("list") && !document["list"].IsBsonNull)
                    {
                        var arr = document["list"].AsBsonArray;
                        if (arr != null && arr.Any())
                        {
                            foreach (var item in arr)
                            {
                                ExternalGuarantee eg = new ExternalGuarantee();
                                eg.seq_no = report.external_guarantees.Count + 1;

                                report.external_guarantees.Add(eg);
                                eg.creditor = item["creditorName"].IsBsonNull ? string.Empty : item["creditorName"].AsString;
                                eg.debtor = item["debtorName"].IsBsonNull ? string.Empty : item["debtorName"].AsString;
                                eg.type = item["priClaSecKind"].IsBsonNull ? string.Empty : item["priClaSecKind"].AsString;
                                if (eg.type == "1")
                                {
                                    eg.type = "合同";
                                }
                                else
                                {
                                    eg.type = "连带责任保证";
                                }
                                eg.amount = item["priClaSecAm"].IsBsonNull ? string.Empty : (item["priClaSecAm"].IsInt32 ? item["priClaSecAm"].AsInt32.ToString() : item["priClaSecAm"].AsDouble.ToString());
                                var startDate = item["pefPerFrom"].IsBsonNull ? string.Empty : this.ConvertStringToDate(item["pefPerFrom"].AsInt64.ToString());
                                var endDate = item["pefPerTo"].IsBsonNull ? string.Empty : this.ConvertStringToDate(item["pefPerTo"].AsInt64.ToString());

                                eg.period = string.Format("{0}至{1}", startDate, endDate);
                                eg.guarantee_time = item["guaranperiod"].IsBsonNull ? string.Empty : item["guaranperiod"].AsString;
                                if (eg.guarantee_time == "1")
                                {
                                    eg.guarantee_time = "期限";
                                }
                                else if (eg.guarantee_time == "2")
                                {
                                    eg.guarantee_time = "连带责任保证";
                                }
                                else if (eg.guarantee_time == "3")
                                {
                                    eg.guarantee_time = "连带责任保证";
                                }
                                eg.guarantee_type = item["gaType"].IsBsonNull ? string.Empty : item["gaType"].AsString;
                                if (eg.guarantee_type == "1")
                                {
                                    eg.guarantee_type = "一般保证";
                                }
                                else if (eg.guarantee_type == "2")
                                {
                                    eg.guarantee_type = "期限";
                                }
                                else if (eg.guarantee_type == "3")
                                {
                                    eg.guarantee_type = "连带责任保证";
                                }
                                if (arr.Count >= 5)
                                {
                                    i++;
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
        #endregion

        #region 企业资产状况信息
        /// <summary>
        /// 企业资产状况信息
        /// </summary>
        void LoadAndParseReportQYZC(HtmlNode table, Report report)
        {
            if (table == null) return;
            var trs = table.SelectNodes("./tr");
            if (trs != null && trs.Any())
            {
                foreach (var tr in trs)
                {
                    var tds = tr.SelectNodes("./td");
                    if (tds != null && tds.Any())
                    {
                        for (int i = 0; i < tds.Count; i += 2)
                        {
                            switch (tds[i].InnerText)
                            {
                                case "资产总额":
                                    report.total_equity = tds[i + 1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Trim();
                                    break;
                                case "负债总额":
                                case "金融贷款":
                                    report.debit_amount = tds[i + 1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Trim();
                                    break;
                                case "销售总额":
                                case "营业总收入":
                                case "销售额或营业收入":
                                    report.sale_income = tds[i + 1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Trim();
                                    break;
                                case "营业总收入中主营业务收入":
                                case "其中：主营业务收入":
                                    report.serv_fare_income = tds[i + 1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Trim();
                                    break;
                                case "利润总额":
                                case "盈余总额":
                                    report.profit_total = tds[i + 1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Trim();
                                    break;
                                case "净利润":
                                    report.net_amount = tds[i + 1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Trim();
                                    break;
                                case "纳税总额":
                                case "纳税金额":
                                    report.tax_total = tds[i + 1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Trim();
                                    break;
                                case "所有者权益合计":
                                case "获得政府扶持资金、补助":
                                    report.profit_reta = tds[i + 1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Trim();
                                    break;
                                default:
                                    break;

                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析深圳信息
        /// <summary>
        /// 解析广州信息
        /// </summary>
        /// <param name="responseData"></param>
        private void LoadAndParseResponse_SZ(ResponseInfo responseInfo)
        {
            if (string.IsNullOrWhiteSpace(responseInfo.Data)) return;
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseInfo.Data);
            HtmlNode rootNode = document.DocumentNode;
            HtmlNode.ElementsFlags.Remove("p");
            HtmlNode.ElementsFlags.Remove("input");
            HtmlNode.ElementsFlags.Remove("form");
            rootNode.OuterHtml.Replace("<br>", "");
            if (responseInfo.Name == "gongshang")
            {
                Parallel.Invoke(new ParallelOptions { MaxDegreeOfParallelism=_parallelCount},
                    () => this.LoadAndParseBasicInfo_SZ(rootNode),
                    () => this.LoadAndParseReport_SZ(rootNode),
                    () => this.LoadAndParsePartnerInfo_SZ(rootNode),
                    () => this.LoadAndParseEmployee_SZ(rootNode),
                    () => this.LoadAndParseChangeRecord_SZ(rootNode),
                    () => this.LoadAndParseBranch_SZ(rootNode),
                    () => this.LoadAndParseFinancialContribution_SZ(rootNode),
                    () => this.LoadAndParseMortgage_SZ(rootNode),
                    () => this.LoadAndParseLicence_SZ(rootNode),
                    () => this.LoadAndParseStockChange_SZ(rootNode),
                    () => this.LoadAndParseCheckup_SZ(rootNode),
                    () => this.LoadAndParseAbnormal_SZ(rootNode),
                    () => this.LoadAndParseAdministrativePunishment_SZ(rootNode));
                //this.LoadAndParseBasicInfo_SZ(rootNode);
                //this.LoadAndParseReport_SZ(rootNode);
                //this.LoadAndParsePartnerInfo_SZ(rootNode);
                //this.LoadAndParseEmployee_SZ(rootNode);
                //this.LoadAndParseChangeRecord_SZ(rootNode);
                //this.LoadAndParseBranch_SZ(rootNode);
                //this.LoadAndParseFinancialContribution_SZ(rootNode);
                //this.LoadAndParseMortgage_SZ(rootNode);
                //this.LoadAndParseLicence_SZ(rootNode);
                ////this.LoadAndParseEquityQuality_SZ(rootNode);
                //this.LoadAndParseStockChange_SZ(rootNode);
                //this.LoadAndParseCheckup_SZ(rootNode);
                //this.LoadAndParseAbnormal_SZ(rootNode);
            }
            
        }
        #endregion

        #region 解析深圳信息
        /// <summary>
        /// 解析广州信息
        /// </summary>
        /// <param name="responseData"></param>
        private void LoadAndParseResponse_SZ_API(ResponseInfo responseInfo)
        {
            if (string.IsNullOrWhiteSpace(responseInfo.Data)) return;
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseInfo.Data);
            HtmlNode rootNode = document.DocumentNode;
            HtmlNode.ElementsFlags.Remove("p");
            HtmlNode.ElementsFlags.Remove("input");
            HtmlNode.ElementsFlags.Remove("form");
            rootNode.OuterHtml.Replace("<br>", "");
            if (responseInfo.Name == "gongshang")
            {
                this.LoadAndParseBasicInfo_SZ(rootNode);
            }
        }
        #endregion

        #region 解析深圳基本信息
        /// <summary>
        /// 解析深圳基本信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseBasicInfo_SZ(HtmlNode rootNode)
        {
            var lis = rootNode.SelectNodes("//div[@id='yyzz']/div[@class='infor_ul']/ul/li");
            if (lis != null && lis.Any())
            {
                foreach (var li in lis)
                {
                    var spans = li.SelectNodes("./span");
                    var title = spans.First().InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                    var val = spans.Last().InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "")
                            .Replace("&nbsp;", "").Replace("&amp;#8226;", "•").Replace("&#8226;", "•"); ;
                    switch (title)
                    {
                        case "注册号":
                        case "统一社会信用代码":
                        case "注册号/统一社会信用代码":
                        case "统一社会信用代码/注册号":
                            if (val.Length == 18)
                                _enterpriseInfo.credit_no = val;
                            else
                                _enterpriseInfo.reg_no = val;
                            break;
                        case "企业（机构）名称":
                        case "名称":
                        case "企业名称":
                            if (string.IsNullOrEmpty(_enterpriseInfo.name))
                                _enterpriseInfo.name = val;
                            break;
                        case "类型":
                            _enterpriseInfo.econ_kind = val;
                            break;
                        case "法定代表人":
                        case "法人代表":
                        case "负责人":
                        case "股东":
                        case "经营者":
                        case "执行事务合伙人":
                        case "投资人":
                            _enterpriseInfo.oper_name = val;
                            break;
                        case "住所":
                        case "经营场所":
                        case "营业场所":
                        case "主要经营场所":
                            Address address = new Address();
                            address.name = "注册地址";
                            address.address = val;
                            address.postcode = "";
                            _enterpriseInfo.addresses.Add(address);
                            break;
                        case "注册资金":
                        case "注册资本":
                        case "成员出资总额":
                            _enterpriseInfo.regist_capi = val;
                            break;
                        case "成立日期":
                        case "登记日期":
                        case "注册日期":
                            _enterpriseInfo.start_date = val;
                            break;
                        case "营业期限自":
                        case "经营期限自":
                        case "合伙期限自":
                            _enterpriseInfo.term_start = val;
                            break;
                        case "营业期限至":
                        case "经营期限至":
                        case "合伙期限至":
                            _enterpriseInfo.term_end = val;
                            break;
                        case "经营范围":
                        case "业务范围":
                            _enterpriseInfo.scope = val;
                            break;
                        case "登记机关":
                            _enterpriseInfo.belong_org = val;
                            break;
                        case "核准日期":
                        case "发照日期":
                            _enterpriseInfo.check_date = val;
                            break;
                        case "登记状态":
                        case "经营状态":
                            _enterpriseInfo.status = val;
                            break;
                        case "吊销日期":
                        case "注销日期":
                            _enterpriseInfo.end_date = val;
                            break;
                        case "组成形式":
                            _enterpriseInfo.type_desc = val;
                            break;
                        default:
                            break;
                    }
                }
            }
        }
        #endregion

        #region 解析深圳股东信息
        /// <summary>
        /// 解析深圳股东信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParsePartnerInfo_SZ(HtmlNode rootNode)
        {
            var request = this.CreateRequest();
            var table = rootNode.SelectSingleNode("//div[@id='UpdatePanel2']/div[@class='item_box']/table");
            if (table != null)
            {
                this.LoadAndParsePartnerContent(table);
                var pageDiv = table.SelectSingleNode("./following-sibling::div[1]/div[@class='zongji']");
                var pages = 1;
                if (pageDiv != null)
                {

                    var content = pageDiv.InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").TrimEnd(new char[] { '页' });
                    var index = content.LastIndexOf("共");
                    pages = int.Parse(content.Substring(index + 1));
                    if (pages > 1)
                    {
                        var ScriptManager1 = string.Format("UpdatePanel2|wucTZRInfo$TurnPageBar1$lbtnNextPage");
                        var txtrid = (rootNode.SelectSingleNode("//input[@id='txtrid']") == null
                                ? rootNode.SelectSingleNode("//input[@id='CompanyInfo_txtrid']")
                                : rootNode.SelectSingleNode("//input[@id='txtrid']")).Attributes["value"].Value;
                        var __EVENTTARGET = string.Format("wucTZRInfo$TurnPageBar1$lbtnNextPage");
                        var __VIEWSTATE = rootNode.SelectSingleNode("//input[@id='__VIEWSTATE']").Attributes["value"].Value;
                        var __VIEWSTATEGENERATOR = rootNode.SelectSingleNode("//input[@id='__VIEWSTATEGENERATOR']").Attributes["value"].Value;
                        var __EVENTVALIDATION = rootNode.SelectSingleNode("//input[@id='__EVENTVALIDATION']").Attributes["value"].Value;

                        request.AddOrUpdateRequestParameter("ScriptManager1", ScriptManager1);
                        request.AddOrUpdateRequestParameter("txtrid", txtrid);
                        request.AddOrUpdateRequestParameter("__EVENTTARGET", __EVENTTARGET);
                        request.AddOrUpdateRequestParameter("__VIEWSTATE", __VIEWSTATE);
                        request.AddOrUpdateRequestParameter("__VIEWSTATEGENERATOR", __VIEWSTATEGENERATOR);
                        request.AddOrUpdateRequestParameter("__EVENTVALIDATION", __EVENTVALIDATION);
                        for (int i = 2; i <= pages; i++)
                        {
                            
                            var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("partner"));
                            if (responseList != null && responseList.Any())
                            { 
                                HtmlDocument hd=new HtmlDocument();
                                hd.LoadHtml(responseList.First().Data);
                                var rt=hd.DocumentNode;
                                var inner_table = rt.SelectSingleNode("//div[@class='item_box']/table");
                                this.LoadAndParsePartnerContent(inner_table);

                                var start = rt.OuterHtml.IndexOf("__VIEWSTATE|");
                                var end = rt.OuterHtml.IndexOf("|0|asyncPostBackControlIDs");
                                var temp = rt.OuterHtml.Substring(start + "__VIEWSTATE|".Length, end - start - "__VIEWSTATE|".Length);
                                var arr = temp.Split('|');
                                if (arr != null && arr.Any())
                                {
                                    __VIEWSTATE = arr.First();
                                    __EVENTVALIDATION = arr.Last();
                                    request.AddOrUpdateRequestParameter("__VIEWSTATE", __VIEWSTATE);
                                    request.AddOrUpdateRequestParameter("__EVENTVALIDATION", __EVENTVALIDATION);
                                }
                               
                            }
                        }
                        
                    }
                }
            }
        }
        #endregion

        #region 解析深圳股东内容
        void LoadAndParsePartnerContent(HtmlNode table)
        {
            var request = this.CreateRequest();
            var trs = table.SelectNodes("./tr");
            if (trs != null && trs.Count > 1)
            {

                trs.Remove(0);
                foreach (var tr in trs)
                {
                    int seqno;
                    var tds = tr.SelectNodes("./td");
                    if (tds != null && tds.Count >= 5)
                    {
                        Partner partner = new Partner();
                        partner.seq_no = _enterpriseInfo.partners.Count + 1;
                        if (int.TryParse(tds.First().InnerText.Replace("\r", "").Replace("\n", "")
                            .Replace(" ", "").Replace("\t", "").Replace("&nbsp;", ""), out seqno))
                        {
                            partner.stock_name = tds[1].InnerText.Replace("\r", "").Replace("\n", "")
                            .Replace(" ", "").Replace("\t", "").Replace("&nbsp;", "");
                            partner.stock_type = tds[2].InnerText.Replace("\r", "").Replace("\n", "")
                            .Replace(" ", "").Replace("\t", "").Replace("&nbsp;", "");
                            partner.identify_type = tds[3].InnerText.Replace("\r", "").Replace("\n", "")
                            .Replace(" ", "").Replace("\t", "").Replace("&nbsp;", "");
                            partner.identify_no = tds[4].InnerText.Replace("\r", "").Replace("\n", "")
                            .Replace(" ", "").Replace("\t", "").Replace("&nbsp;", "");
                        }
                        else
                        {
                            partner.stock_name = tds[0].InnerText.Replace("\r", "").Replace("\n", "")
                            .Replace(" ", "").Replace("\t", "").Replace("&nbsp;", "");
                            partner.stock_type = tds[1].InnerText.Replace("\r", "").Replace("\n", "")
                            .Replace(" ", "").Replace("\t", "").Replace("&nbsp;", "");
                            partner.identify_type = tds[2].InnerText.Replace("\r", "").Replace("\n", "")
                            .Replace(" ", "").Replace("\t", "").Replace("&nbsp;", "");
                            partner.identify_no = tds[3].InnerText.Replace("\r", "").Replace("\n", "")
                            .Replace(" ", "").Replace("\t", "").Replace("&nbsp;", "");
                        }
                        var aNode = tds.Last().SelectSingleNode("./a");
                        if (aNode != null)
                        {
                            var id = tds.Last().Attributes.Contains("data") ? tds.Last().Attributes["data"].Value : string.Empty;
                            var recordid = tds.Last().Attributes.Contains("info") ? tds.Last().Attributes["info"].Value : string.Empty;
                            request.AddOrUpdateRequestParameter("partner_detail_id", id);
                            request.AddOrUpdateRequestParameter("partner_detail_recordid", recordid);
                            var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("partner_detail"));
                            if (responseList != null && responseList.Any())
                            {
                                this.LoadAndParsePartnerDetail_SZ(responseList.First().Data, partner);
                            }
                        }
                        _enterpriseInfo.partners.Add(partner);
                    }
                }
            }
                
        }
        #endregion

        #region 解析深圳股东详情信息
        /// <summary>
        /// 解析深圳股东详情信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="partner"></param>
        void LoadAndParsePartnerDetail_SZ(string responseData, Partner partner)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            var table_gdxx = rootNode.SelectSingleNode("//div[@id='gdxx']/table");
            var table_rjmx = rootNode.SelectSingleNode("//div[@id='rjmx']/table");
            var table_sjmx = rootNode.SelectSingleNode("//div[@id='sjmx']/table");

            if (table_rjmx != null)
            {
                var trs = table_rjmx.SelectNodes("./tr");
                if (trs != null && trs.Count > 1)
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Count == 3)
                        {
                            ShouldCapiItem sci = new ShouldCapiItem();
                            sci.invest_type = tds[0].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            sci.shoud_capi = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            sci.should_capi_date = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            if (!string.IsNullOrWhiteSpace(sci.shoud_capi))
                            {
                                partner.should_capi_items.Add(sci);
                            }

                        }
                    }
                }
            }
            if (table_sjmx != null)
            {
                var trs = table_sjmx.SelectNodes("./tr");
                if (trs != null && trs.Count > 1)
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Count == 3)
                        {
                            RealCapiItem rci = new RealCapiItem();
                            rci.invest_type = tds[0].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            rci.real_capi = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            rci.real_capi_date = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            if (!string.IsNullOrWhiteSpace(rci.real_capi))
                            {
                                partner.real_capi_items.Add(rci);
                            }

                        }
                    }
                }
            }
            if (table_gdxx != null)
            {
                var trs = table_gdxx.SelectNodes("./tr");
                if (trs != null && trs.Count == 3)
                {
                    partner.total_should_capi = trs[1].SelectNodes("./td").Last().InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                }
            }
            if (table_gdxx != null)
            {
                var trs = table_gdxx.SelectNodes("./tr");
                if (trs != null && trs.Count == 3)
                {
                    partner.total_real_capi = trs[2].SelectNodes("./td").Last().InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                }
            }
        }
        #endregion

        #region 解析深圳主要人员信息
        /// <summary>
        /// 解析深圳主要人员信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseEmployee_SZ(HtmlNode rootNode)
        {
            var lis = rootNode.SelectNodes("//div[@class='main_tabs_box']/div/div/div/div/div[@id='PeopleMain']/div[@id='MainPeople']/ul/li");
            if (lis != null && lis.Any())
            {
                foreach (var li in lis)
                {
                    var ps = li.SelectNodes("./p");
                    if (ps != null)
                    {
                        Employee employee = new Employee();
                        employee.seq_no = _enterpriseInfo.employees.Count + 1;
                        if (ps.Count == 1)
                        {
                            employee.name = ps.First().InnerText;
                        }
                        else if (ps.Count == 2)
                        {
                            employee.name = ps.First().InnerText;
                            employee.job_title = ps.Last().InnerText;
                        }
                        _enterpriseInfo.employees.Add(employee);
                    }
                }
            }
        }
        #endregion

        #region 解析深圳变更信息
        /// <summary>
        /// 解析深圳变更信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseChangeRecord_SZ(HtmlNode rootNode)
        {
            var request = this.CreateRequest();
            var start = rootNode.OuterHtml.IndexOf("<!--变更信息-->");
            var end = rootNode.OuterHtml.IndexOf("<!--动产抵押-->");
            if (start > 0 && start > 0 && end > start)
            {
                var html = rootNode.OuterHtml.Substring(start + "<!--变更信息-->".Length, end - start);
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(html);
                var rd = document.DocumentNode;
                var table = rd.SelectSingleNode("//div[@id='bgxx']/table");
                this.LoadAndParseChangeRecordContent_SZ(table);
                if (table != null)
                {
                    var pageDiv = table.SelectSingleNode("./following-sibling::div[1]/div[@class='zongji']");
                    var pages = 1;
                    if (pageDiv != null)
                    {

                        var content = pageDiv.InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").TrimEnd(new char[] { '页' });
                        var index = content.LastIndexOf("共");
                        pages = int.Parse(content.Substring(index + 1));
                        if (pages > 1)
                        {
                            var ScriptManager1 = string.Format("UpdatePanel5|wucAlterItem$TurnPageBar1$lbtnNextPage");
                            var txtrid = (rootNode.SelectSingleNode("//input[@id='txtrid']") == null
                                ? rootNode.SelectSingleNode("//input[@id='CompanyInfo_txtrid']") 
                                : rootNode.SelectSingleNode("//input[@id='txtrid']")).Attributes["value"].Value;
                            var __EVENTTARGET = string.Format("wucAlterItem$TurnPageBar1$lbtnNextPage");
                            var __VIEWSTATE = rootNode.SelectSingleNode("//input[@id='__VIEWSTATE']").Attributes["value"].Value;
                            var __VIEWSTATEGENERATOR = rootNode.SelectSingleNode("//input[@id='__VIEWSTATEGENERATOR']").Attributes["value"].Value;
                            var __EVENTVALIDATION = rootNode.SelectSingleNode("//input[@id='__EVENTVALIDATION']").Attributes["value"].Value;

                            request.AddOrUpdateRequestParameter("ScriptManager1", ScriptManager1);
                            request.AddOrUpdateRequestParameter("txtrid", txtrid);
                            request.AddOrUpdateRequestParameter("__EVENTTARGET", __EVENTTARGET);
                            request.AddOrUpdateRequestParameter("__VIEWSTATE", __VIEWSTATE);
                            request.AddOrUpdateRequestParameter("__VIEWSTATEGENERATOR", __VIEWSTATEGENERATOR);
                            request.AddOrUpdateRequestParameter("__EVENTVALIDATION", __EVENTVALIDATION);
                            for (int i = 2; i <= pages; i++)
                            {
                                
                                var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("changerecord"));
                                if (responseList != null && responseList.Any())
                                {
                                    HtmlDocument hd = new HtmlDocument();
                                    hd.LoadHtml(responseList.First().Data);
                                    var rt = hd.DocumentNode;
                                    var inner_table = rt.SelectSingleNode("//div[@class='item_box']/table");
                                    this.LoadAndParseChangeRecordContent_SZ(inner_table);

                                    var inner_start = rt.OuterHtml.IndexOf("__VIEWSTATE|");
                                    var inner_end = rt.OuterHtml.IndexOf("|0|asyncPostBackControlIDs");
                                    var temp = rt.OuterHtml.Substring(inner_start + "__VIEWSTATE|".Length, inner_end - inner_start - "__VIEWSTATE|".Length);
                                    var arr = temp.Split('|');
                                    if (arr != null && arr.Any())
                                    {
                                        __VIEWSTATE = arr.First();
                                        __EVENTVALIDATION = arr.Last();
                                        request.AddOrUpdateRequestParameter("__VIEWSTATE", __VIEWSTATE);
                                        request.AddOrUpdateRequestParameter("__EVENTVALIDATION", __EVENTVALIDATION);
                                    }
                                }
                            }

                        }
                    }
                }
            }
            
        }
        #endregion

        #region 解析深圳市变更信息内容
        /// <summary>
        /// 解析深圳市变更信息内容
        /// </summary>
        /// <param name="table"></param>
        void LoadAndParseChangeRecordContent_SZ(HtmlNode table)
        {
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any() && trs.Count > 1)
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        int seqno;
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Any() && tds.Count>=4)
                        {
                            ChangeRecord changeRecord = new ChangeRecord();
                            if (int.TryParse(tds.First().InnerText.Replace("\r", "").Replace("\n", "")
                                .Replace(" ", "").Replace("\t", "").Replace("&nbsp;", ""), out seqno))
                            {
                                changeRecord.seq_no = seqno;
                                changeRecord.change_item = tds[1].InnerText.Replace("\r", "").Replace("\n", "")
                        .Replace(" ", "").Replace("\t", "").Replace("&nbsp;", ""); 
                                changeRecord.before_content = tds[2].InnerText.Replace("\r", "").Replace("\n", "")
                        .Replace(" ", "").Replace("\t", "").Replace("&nbsp;", ""); 
                                changeRecord.after_content = tds[3].InnerText.Replace("\r", "").Replace("\n", "")
                        .Replace(" ", "").Replace("\t", "").Replace("&nbsp;", ""); 
                                changeRecord.change_date = tds[4].InnerText.Replace("\r", "").Replace("\n", "")
                        .Replace(" ", "").Replace("\t", "").Replace("&nbsp;", ""); 
                            }
                            else
                            {
                                changeRecord.seq_no = _enterpriseInfo.changerecords.Count + 1;
                                changeRecord.change_item = tds[0].InnerText.Replace("\r", "").Replace("\n", "")
                        .Replace(" ", "").Replace("\t", "").Replace("&nbsp;", ""); 
                                changeRecord.before_content = tds[1].InnerText.Replace("\r", "").Replace("\n", "")
                        .Replace(" ", "").Replace("\t", "").Replace("&nbsp;", ""); 
                                changeRecord.after_content = tds[2].InnerText.Replace("\r", "").Replace("\n", "")
                        .Replace(" ", "").Replace("\t", "").Replace("&nbsp;", ""); 
                                changeRecord.change_date = tds[3].InnerText.Replace("\r", "").Replace("\n", "")
                        .Replace(" ", "").Replace("\t", "").Replace("&nbsp;", ""); 
                            }
                            _enterpriseInfo.changerecords.Add(changeRecord);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析深圳分支机构信息
        void LoadAndParseBranch_SZ(HtmlNode rootNode)
        {
            var lis = rootNode.SelectNodes("//div[@class='main_tabs_box']/div/div/div/div/div[@id='InformationOfAffiliatedAgency']/div[@class='web_ul']/ul/li");
            if (lis != null && lis.Any())
            {
                foreach (var li in lis)
                {
                    var h4 = li.SelectSingleNode("./h4");
                    var ps = li.SelectNodes("./p");
                    Branch branch = new Branch();
                    branch.seq_no = _enterpriseInfo.branches.Count + 1;
                    branch.name = h4.InnerText.Replace("\r", "").Replace("\n", "")
                        .Replace(" ", "").Replace("\t", "").Replace("&nbsp;", "");
                    branch.reg_no = ps.First().SelectSingleNode("./span").InnerText.Replace("\r", "").Replace("\n", "")
                        .Replace(" ", "").Replace("\t", "").Replace("&nbsp;", "");
                    branch.belong_org = ps.Last().SelectSingleNode("./span") == null ? li.SelectSingleNode("./span").InnerText.Replace("\r", "").Replace("\n", "")
                        .Replace(" ", "").Replace("\t", "").Replace("&nbsp;", "") : ps.Last().SelectSingleNode("./span").InnerText.Replace("\r", "").Replace("\n", "")
                        .Replace(" ", "").Replace("\t", "").Replace("&nbsp;", "");
                    _enterpriseInfo.branches.Add(branch);
                }
            }
        }
        #endregion

        #region 解析深圳动产抵押登记信息
        /// <summary>
        /// 解析深圳动产抵押登记信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseMortgage_SZ(HtmlNode rootNode)
        {
            var request = this.CreateRequest();
            var divs = rootNode.SelectNodes("//div[@id='BaseInfo']/div");
            if (divs != null && divs.Any())
            {
                foreach (var div in divs)
                {
                    if (div.InnerText.Contains("动产抵押登记信息"))
                    {
                        var table = div.SelectSingleNode("./div[@class='item_box']/table");
                        this.LoadAndParseMortgageContent_SZ(table);
                        if (table != null)
                        {
                            var pageDiv = table.SelectSingleNode("./following-sibling::div[1]/div[@class='zongji']");
                            var pages = 1;
                            if (pageDiv != null)
                            {

                                var content = pageDiv.InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").TrimEnd(new char[] { '页' });
                                var index = content.LastIndexOf("共");
                                pages = int.Parse(content.Substring(index + 1));
                                if (pages > 1)
                                {
                                    var ScriptManager1 = string.Format("UpdatePanel12|wucDCDYInfo$TurnPageBar1$lbtnNextPage");
                                    var txtrid = (rootNode.SelectSingleNode("//input[@id='txtrid']") == null
                                ? rootNode.SelectSingleNode("//input[@id='CompanyInfo_txtrid']")
                                : rootNode.SelectSingleNode("//input[@id='txtrid']")).Attributes["value"].Value;
                                    var __EVENTTARGET = string.Format("wucDCDYInfo$TurnPageBar1$lbtnNextPage");
                                    var __VIEWSTATE = rootNode.SelectSingleNode("//input[@id='__VIEWSTATE']").Attributes["value"].Value;
                                    var __VIEWSTATEGENERATOR = rootNode.SelectSingleNode("//input[@id='__VIEWSTATEGENERATOR']").Attributes["value"].Value;
                                    var __EVENTVALIDATION = rootNode.SelectSingleNode("//input[@id='__EVENTVALIDATION']").Attributes["value"].Value;

                                    request.AddOrUpdateRequestParameter("ScriptManager1", ScriptManager1);
                                    request.AddOrUpdateRequestParameter("txtrid", txtrid);
                                    request.AddOrUpdateRequestParameter("__EVENTTARGET", __EVENTTARGET);
                                    request.AddOrUpdateRequestParameter("__VIEWSTATE", __VIEWSTATE);
                                    request.AddOrUpdateRequestParameter("__VIEWSTATEGENERATOR", __VIEWSTATEGENERATOR);
                                    request.AddOrUpdateRequestParameter("__EVENTVALIDATION", __EVENTVALIDATION);
                                    for (int i = 2; i <= pages; i++)
                                    {


                                        var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("mortgage"));
                                        if (responseList != null && responseList.Any())
                                        {
                                            HtmlDocument hd = new HtmlDocument();
                                            hd.LoadHtml(responseList.First().Data);
                                            var rt = hd.DocumentNode;
                                            var inner_table = rt.SelectSingleNode("//div[@class='item_box']/table");
                                            this.LoadAndParseMortgageContent_SZ(inner_table);

                                            var inner_start = rt.OuterHtml.IndexOf("__VIEWSTATE|");
                                            var inner_end = rt.OuterHtml.IndexOf("|0|asyncPostBackControlIDs");
                                            var temp = rt.OuterHtml.Substring(inner_start + "__VIEWSTATE|".Length, inner_end - inner_start - "__VIEWSTATE|".Length);
                                            var arr = temp.Split('|');
                                            if (arr != null && arr.Any())
                                            {
                                                __VIEWSTATE = arr.First();
                                                __EVENTVALIDATION = arr.Last();
                                                request.AddOrUpdateRequestParameter("__VIEWSTATE", __VIEWSTATE);
                                                request.AddOrUpdateRequestParameter("__EVENTVALIDATION", __EVENTVALIDATION);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        break;
                    }
                }
            }
        }
        #endregion

        #region 解析深圳市动产抵押信息内容
        /// <summary>
        /// 解析深圳市动产抵押信息内容
        /// </summary>
        /// <param name="table"></param>
        void LoadAndParseMortgageContent_SZ(HtmlNode table)
        {
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any() && trs.Count > 1)
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Any() && tds.Count == 11)
                        {
                            MortgageInfo mortgageInfo = new MortgageInfo();
                            mortgageInfo.seq_no = _enterpriseInfo.mortgages.Count + 1;
                            mortgageInfo.number = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            mortgageInfo.date = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            mortgageInfo.department = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            mortgageInfo.amount = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            mortgageInfo.status = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            mortgageInfo.public_date = tds[6].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            _enterpriseInfo.mortgages.Add(mortgageInfo);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析深圳股权出质登机信息
        /// <summary>
        /// 解析深圳股权出质登机信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseEquityQuality_SZ(HtmlNode rootNode)
        {
            var request = this.CreateRequest();
            var divs = rootNode.SelectNodes("//div[@id='BaseInfo']/div");
            if (divs != null && divs.Any())
            {
                foreach (var div in divs)
                {
                    if (div.InnerText.Contains("股权出质登记信息"))
                    {
                        var table = div.SelectSingleNode("./div[@class='item_box']/table");
                        this.LoadAndParseEquityQualityContent_SZ(table);
                        if (table != null)
                        {
                            var pageDiv = table.SelectSingleNode("./following-sibling::div[1]/div[@class='zongji']");
                            var pages = 1;
                            if (pageDiv != null)
                            {

                                var content = pageDiv.InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").TrimEnd(new char[] { '页' });
                                var index = content.LastIndexOf("共");
                                pages = int.Parse(content.Substring(index + 1));
                                if (pages > 1)
                                {
                                    var ScriptManager1 = string.Format("UpdatePanel13|wucGQZYInfo$TurnPageBar1$lbtnNextPage");
                                    var txtrid = (rootNode.SelectSingleNode("//input[@id='txtrid']") == null
                                ? rootNode.SelectSingleNode("//input[@id='CompanyInfo_txtrid']")
                                : rootNode.SelectSingleNode("//input[@id='txtrid']")).Attributes["value"].Value;
                                    var __EVENTTARGET = string.Format("wucGQZYInfo$TurnPageBar1$lbtnNextPage");
                                    var __VIEWSTATE = rootNode.SelectSingleNode("//input[@id='__VIEWSTATE']").Attributes["value"].Value;
                                    var __VIEWSTATEGENERATOR = rootNode.SelectSingleNode("//input[@id='__VIEWSTATEGENERATOR']").Attributes["value"].Value;
                                    var __EVENTVALIDATION = rootNode.SelectSingleNode("//input[@id='__EVENTVALIDATION']").Attributes["value"].Value;

                                    request.AddOrUpdateRequestParameter("ScriptManager1", ScriptManager1);
                                    request.AddOrUpdateRequestParameter("txtrid", txtrid);
                                    request.AddOrUpdateRequestParameter("__EVENTTARGET", __EVENTTARGET);
                                    request.AddOrUpdateRequestParameter("__VIEWSTATE", __VIEWSTATE);
                                    request.AddOrUpdateRequestParameter("__VIEWSTATEGENERATOR", __VIEWSTATEGENERATOR);
                                    request.AddOrUpdateRequestParameter("__EVENTVALIDATION", __EVENTVALIDATION);
                                    for (int i = 2; i <= pages; i++)
                                    {


                                        var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("equity_quality"));
                                        if (responseList != null && responseList.Any())
                                        {
                                            HtmlDocument hd = new HtmlDocument();
                                            hd.LoadHtml(responseList.First().Data);
                                            var rt = hd.DocumentNode;
                                            var inner_table = rt.SelectSingleNode("//div[@class='item_box']/table");
                                            this.LoadAndParseEquityQualityContent_SZ(inner_table);

                                            var inner_start = rt.OuterHtml.IndexOf("__VIEWSTATE|");
                                            var inner_end = rt.OuterHtml.IndexOf("|0|asyncPostBackControlIDs");
                                            var temp = rt.OuterHtml.Substring(inner_start + "__VIEWSTATE|".Length, inner_end - inner_start - "__VIEWSTATE|".Length);
                                            var arr = temp.Split('|');
                                            if (arr != null && arr.Any())
                                            {
                                                __VIEWSTATE = arr.First();
                                                __EVENTVALIDATION = arr.Last();
                                                request.AddOrUpdateRequestParameter("__VIEWSTATE", __VIEWSTATE);
                                                request.AddOrUpdateRequestParameter("__EVENTVALIDATION", __EVENTVALIDATION);
                                            }
                                        }
                                    }

                                }
                            }
                        }
                        break;
                    }
                }
            }
        }
        #endregion

        #region 解析深圳市股权出质信息内容
        /// <summary>
        /// 解析深圳市股权出质信息内容
        /// </summary>
        /// <param name="table"></param>
        void LoadAndParseEquityQualityContent_SZ(HtmlNode table)
        {
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any() && trs.Count > 1)
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Any() && tds.Count == 11)
                        {
                            EquityQuality eq = new EquityQuality();

                            eq.seq_no = _enterpriseInfo.equity_qualities.Count + 1;
                            eq.number = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            eq.pledgor = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            eq.pledgor_identify_no = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            eq.pledgor_amount = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            eq.pawnee = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            eq.pawnee_identify_no = tds[6].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            eq.date = tds[7].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            eq.status = tds[8].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            eq.public_date = tds[9].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            _enterpriseInfo.equity_qualities.Add(eq);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析深圳抽查检查信息
        /// <summary>
        /// 解析深圳抽查检查信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseCheckup_SZ(HtmlNode rootNode)
        {
            var request = this.CreateRequest();
            var divs = rootNode.SelectNodes("//div[@id='BaseInfo']/div");
            if (divs != null && divs.Any())
            {
                foreach (var div in divs)
                {
                    if (div.InnerText.Contains("抽查检查信息"))
                    {
                        var table = div.SelectSingleNode("./div[@class='item_box']/table");
                        this.LoadAndParseCheckupContent_SZ(table);
                        if (table != null)
                        {
                            var pageDiv = table.SelectSingleNode("./following-sibling::div[1]/div[@class='zongji']");
                            var pages = 1;
                            if (pageDiv != null)
                            {

                                var content = pageDiv.InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").TrimEnd(new char[] { '页' });
                                var index = content.LastIndexOf("共");
                                pages = int.Parse(content.Substring(index + 1));
                                if (pages > 1)
                                {
                                    var ScriptManager1 = string.Format("UpdatePanel10|wucCCJCInfo$TurnPageBar1$lbtnNextPage");
                                    var txtrid = (rootNode.SelectSingleNode("//input[@id='txtrid']") == null
                                ? rootNode.SelectSingleNode("//input[@id='CompanyInfo_txtrid']")
                                : rootNode.SelectSingleNode("//input[@id='txtrid']")).Attributes["value"].Value;
                                    var __EVENTTARGET = string.Format("wucCCJCInfo$TurnPageBar1$lbtnNextPage");
                                    var __VIEWSTATE = rootNode.SelectSingleNode("//input[@id='__VIEWSTATE']").Attributes["value"].Value;
                                    var __VIEWSTATEGENERATOR = rootNode.SelectSingleNode("//input[@id='__VIEWSTATEGENERATOR']").Attributes["value"].Value;
                                    var __EVENTVALIDATION = rootNode.SelectSingleNode("//input[@id='__EVENTVALIDATION']").Attributes["value"].Value;

                                    request.AddOrUpdateRequestParameter("ScriptManager1", ScriptManager1);
                                    request.AddOrUpdateRequestParameter("txtrid", txtrid);
                                    request.AddOrUpdateRequestParameter("__EVENTTARGET", __EVENTTARGET);
                                    request.AddOrUpdateRequestParameter("__VIEWSTATE", __VIEWSTATE);
                                    request.AddOrUpdateRequestParameter("__VIEWSTATEGENERATOR", __VIEWSTATEGENERATOR);
                                    request.AddOrUpdateRequestParameter("__EVENTVALIDATION", __EVENTVALIDATION);
                                    for (int i = 2; i <= pages; i++)
                                    {


                                        var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("mortgage"));
                                        if (responseList != null && responseList.Any())
                                        {
                                            HtmlDocument hd = new HtmlDocument();
                                            hd.LoadHtml(responseList.First().Data);
                                            var rt = hd.DocumentNode;
                                            var inner_table = rt.SelectSingleNode("//div[@class='item_box']/table");
                                            this.LoadAndParseCheckupContent_SZ(inner_table);

                                            var inner_start = rt.OuterHtml.IndexOf("__VIEWSTATE|");
                                            var inner_end = rt.OuterHtml.IndexOf("|0|asyncPostBackControlIDs");
                                            var temp = rt.OuterHtml.Substring(inner_start + "__VIEWSTATE|".Length, inner_end - inner_start - "__VIEWSTATE|".Length);
                                            var arr = temp.Split('|');
                                            if (arr != null && arr.Any())
                                            {
                                                __VIEWSTATE = arr.First();
                                                __EVENTVALIDATION = arr.Last();
                                                request.AddOrUpdateRequestParameter("__VIEWSTATE", __VIEWSTATE);
                                                request.AddOrUpdateRequestParameter("__EVENTVALIDATION", __EVENTVALIDATION);
                                            }
                                        }
                                    }

                                }
                            }
                        }
                        break;
                    }
                }
            }
        }
        #endregion

        #region 解析深圳市抽查检查信息内容
        /// <summary>
        /// 解析深圳市动产抵押信息内容
        /// </summary>
        /// <param name="table"></param>
        void LoadAndParseCheckupContent_SZ(HtmlNode table)
        {
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any() && trs.Count > 1)
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Any() && tds.Count == 5)
                        {
                            CheckupInfo checkupInfo = new CheckupInfo();
                            checkupInfo.name = _enterpriseInfo.name;
                            checkupInfo.province = _enterpriseInfo.province;
                            checkupInfo.reg_no = string.IsNullOrWhiteSpace(_enterpriseInfo.reg_no) ? _enterpriseInfo.credit_no : _enterpriseInfo.reg_no;
                            checkupInfo.department = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            checkupInfo.date = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            checkupInfo.result = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            
                            _checkups.Add(checkupInfo);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析深圳股东及出资信息
        /// <summary>
        /// 解析深圳股东及出资信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseFinancialContribution_SZ(HtmlNode rootNode)
        {
            var request = this.CreateRequest();
            var divs = rootNode.SelectNodes("//div[@id='BaseInfo']/div");
            if (divs != null && divs.Any())
            {
                foreach (var div in divs)
                {
                    if (div.InnerText.Contains("股东及出资信息") && div.InnerText.Contains("认缴明细") && div.InnerText.Contains("实缴明细"))
                    {
                        var table = div.SelectSingleNode("./div[@class='item_box']/table");
                        this.LoadAndParseFinancialContributionContent_SZ(table);
                        if (table != null)
                        {
                            var pageDiv = table.SelectSingleNode("./following-sibling::div[1]/div[@class='zongji']");
                            var pages = 1;
                            if (pageDiv != null)
                            {

                                var content = pageDiv.InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").TrimEnd(new char[] { '页' });
                                var index = content.LastIndexOf("共");
                                pages = int.Parse(content.Substring(index + 1));
                                if (pages > 1)
                                {
                                    var ScriptManager1 = string.Format("UpdatePanel1|wucGDJCZXX$TurnPageBar1$lbtnNextPage");
                                    var txtrid = (rootNode.SelectSingleNode("//input[@id='txtrid']") == null
                                ? rootNode.SelectSingleNode("//input[@id='CompanyInfo_txtrid']")
                                : rootNode.SelectSingleNode("//input[@id='txtrid']")).Attributes["value"].Value;
                                    var __EVENTTARGET = string.Format("wucGDJCZXX$TurnPageBar1$lbtnNextPage");
                                    var __VIEWSTATE = rootNode.SelectSingleNode("//input[@id='__VIEWSTATE']").Attributes["value"].Value;
                                    var __VIEWSTATEGENERATOR = rootNode.SelectSingleNode("//input[@id='__VIEWSTATEGENERATOR']").Attributes["value"].Value;
                                    var __EVENTVALIDATION = rootNode.SelectSingleNode("//input[@id='__EVENTVALIDATION']").Attributes["value"].Value;

                                    request.AddOrUpdateRequestParameter("ScriptManager1", ScriptManager1);
                                    request.AddOrUpdateRequestParameter("txtrid", txtrid);
                                    request.AddOrUpdateRequestParameter("__EVENTTARGET", __EVENTTARGET);
                                    request.AddOrUpdateRequestParameter("__VIEWSTATE", __VIEWSTATE);
                                    request.AddOrUpdateRequestParameter("__VIEWSTATEGENERATOR", __VIEWSTATEGENERATOR);
                                    request.AddOrUpdateRequestParameter("__EVENTVALIDATION", __EVENTVALIDATION);
                                    for (int i = 2; i <= pages; i++)
                                    {


                                        var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("financial_contribution"));
                                        if (responseList != null && responseList.Any())
                                        {
                                            HtmlDocument hd = new HtmlDocument();
                                            hd.LoadHtml(responseList.First().Data);
                                            var rt = hd.DocumentNode;
                                            var inner_table = rt.SelectSingleNode("//div[@class='item_box']/table");
                                            this.LoadAndParseFinancialContributionContent_SZ(inner_table);

                                            var inner_start = rt.OuterHtml.IndexOf("__VIEWSTATE|");
                                            var inner_end = rt.OuterHtml.IndexOf("|0|asyncPostBackControlIDs");
                                            var temp = rt.OuterHtml.Substring(inner_start + "__VIEWSTATE|".Length, inner_end - inner_start - "__VIEWSTATE|".Length);
                                            var arr = temp.Split('|');
                                            if (arr != null && arr.Any())
                                            {
                                                __VIEWSTATE = arr.First();
                                                __EVENTVALIDATION = arr.Last();
                                                request.AddOrUpdateRequestParameter("__VIEWSTATE", __VIEWSTATE);
                                                request.AddOrUpdateRequestParameter("__EVENTVALIDATION", __EVENTVALIDATION);
                                            }
                                        }
                                    }

                                }
                            }
                        }
                        break;
                    }
                }
            }
        }
        #endregion

        #region 解析深圳市股东及出资信息内容
        /// <summary>
        /// 解析深圳市变更信息内容
        /// </summary>
        /// <param name="table"></param>
        void LoadAndParseFinancialContributionContent_SZ(HtmlNode table)
        {
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any() && trs.Count > 1)
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Any() && tds.Count == 11)
                        {
                            FinancialContribution fc = new FinancialContribution();
                            fc.seq_no = _enterpriseInfo.financial_contributions.Count + 1;
                            fc.investor_name = tds[0].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            fc.total_should_capi = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            fc.total_real_capi = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            FinancialContribution.ShouldCapiItem sci = new FinancialContribution.ShouldCapiItem();
                            sci.should_invest_type = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            sci.should_capi = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            sci.should_invest_date = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            sci.public_date = tds[6].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            fc.should_capi_items.Add(sci);

                            FinancialContribution.RealCapiItem rci = new FinancialContribution.RealCapiItem();
                            rci.real_invest_type = tds[7].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            rci.real_capi = tds[8].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            rci.real_invest_date = tds[9].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            rci.public_date = tds[10].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            fc.real_capi_items.Add(rci);
                            _enterpriseInfo.financial_contributions.Add(fc);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析深圳股权变更信息
        /// <summary>
        /// 解析深圳股权变更信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseStockChange_SZ(HtmlNode rootNode)
        {
            var request = this.CreateRequest();
            var divs = rootNode.SelectNodes("//div[@id='BaseInfo']/div");
            if (divs != null && divs.Any())
            {
                foreach (var div in divs)
                {
                    if (div.InnerText.Contains("股权变更信息"))
                    {
                        var table = div.SelectSingleNode("./div[@class='item_box']/table");
                        this.LoadAndParseStockChangeContent_SZ(table);
                        if (table != null)
                        {
                            var pageDiv = table.SelectSingleNode("./following-sibling::div[1]/div[@class='zongji']");
                            var pages = 1;
                            if (pageDiv != null)
                            {

                                var content = pageDiv.InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").TrimEnd(new char[] { '页' });
                                var index = content.LastIndexOf("共");
                                pages = int.Parse(content.Substring(index + 1));
                                if (pages > 1)
                                {
                                    var ScriptManager1 = string.Format("UpdatePanel3|wucGQBGXX$TurnPageBar1$lbtnNextPage");
                                    var txtrid = (rootNode.SelectSingleNode("//input[@id='txtrid']") == null
                                ? rootNode.SelectSingleNode("//input[@id='CompanyInfo_txtrid']")
                                : rootNode.SelectSingleNode("//input[@id='txtrid']")).Attributes["value"].Value;
                                    var __EVENTTARGET = string.Format("wucGQBGXX$TurnPageBar1$lbtnNextPage");
                                    var __VIEWSTATE = rootNode.SelectSingleNode("//input[@id='__VIEWSTATE']").Attributes["value"].Value;
                                    var __VIEWSTATEGENERATOR = rootNode.SelectSingleNode("//input[@id='__VIEWSTATEGENERATOR']").Attributes["value"].Value;
                                    var __EVENTVALIDATION = rootNode.SelectSingleNode("//input[@id='__EVENTVALIDATION']").Attributes["value"].Value;

                                    request.AddOrUpdateRequestParameter("ScriptManager1", ScriptManager1);
                                    request.AddOrUpdateRequestParameter("txtrid", txtrid);
                                    request.AddOrUpdateRequestParameter("__EVENTTARGET", __EVENTTARGET);
                                    request.AddOrUpdateRequestParameter("__VIEWSTATE", __VIEWSTATE);
                                    request.AddOrUpdateRequestParameter("__VIEWSTATEGENERATOR", __VIEWSTATEGENERATOR);
                                    request.AddOrUpdateRequestParameter("__EVENTVALIDATION", __EVENTVALIDATION);
                                    for (int i = 2; i <= pages; i++)
                                    {

                                        try
                                        {
                                            var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("stock_change"));
                                            if (responseList != null && responseList.Any())
                                            {
                                                HtmlDocument hd = new HtmlDocument();
                                                hd.LoadHtml(responseList.First().Data);
                                                var rt = hd.DocumentNode;
                                                var inner_table = rt.SelectSingleNode("//div[@class='item_box']/table");
                                                this.LoadAndParseStockChangeContent_SZ(inner_table);

                                                var inner_start = rt.OuterHtml.IndexOf("__VIEWSTATE|");
                                                var inner_end = rt.OuterHtml.IndexOf("|0|asyncPostBackControlIDs");
                                                var temp = rt.OuterHtml.Substring(inner_start + "__VIEWSTATE|".Length, inner_end - inner_start - "__VIEWSTATE|".Length);
                                                var arr = temp.Split('|');
                                                if (arr != null && arr.Any())
                                                {
                                                    __VIEWSTATE = arr.First();
                                                    __EVENTVALIDATION = arr.Last();
                                                    request.AddOrUpdateRequestParameter("__VIEWSTATE", __VIEWSTATE);
                                                    request.AddOrUpdateRequestParameter("__EVENTVALIDATION", __EVENTVALIDATION);
                                                }
                                            }
                                        }
                                        catch { break; }
                                    }

                                }
                            }
                        }
                        break;
                    }
                }
            }
        }
        #endregion

        #region 解析深圳市股权变更信息内容
        /// <summary>
        /// 解析深圳市变更信息内容
        /// </summary>
        /// <param name="table"></param>
        void LoadAndParseStockChangeContent_SZ(HtmlNode table)
        {
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any() && trs.Count > 1)
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Any() && tds.Count == 6)
                        {
                            StockChangeItem sci = new StockChangeItem();
                            sci.seq_no = _enterpriseInfo.stock_changes.Count + 1;
                            sci.name = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            sci.before_percent = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            sci.after_percent = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            sci.change_date = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            sci.public_date = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            _enterpriseInfo.stock_changes.Add(sci);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析深圳行政许可信息
        /// <summary>
        /// 解析深圳股权变更信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseLicence_SZ(HtmlNode rootNode)
        {
            var request = this.CreateRequest();
            var divs = rootNode.SelectNodes("//div[@id='BaseInfo']/div");
            if (divs != null && divs.Any())
            {
                foreach (var div in divs)
                {
                    if (div.InnerText.Contains("行政许可信息"))
                    {
                        var table = div.SelectSingleNode("./div[@class='item_box']/table");
                        this.LoadAndParseLicenceContent_SZ(table);
                        if (table != null)
                        {
                            var pageDiv = table.SelectSingleNode("./following-sibling::div[1]/div[@class='zongji']");
                            var pages = 1;
                            if (pageDiv != null)
                            {

                                var content = pageDiv.InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").TrimEnd(new char[] { '页' });
                                var index = content.LastIndexOf("共");
                                pages = int.Parse(content.Substring(index + 1));
                                if (pages > 1)
                                {
                                    var ScriptManager1 = string.Format("UpdatePanel4|wucXZXKXX$TurnPageBar1$lbtnNextPage");
                                    var txtrid = (rootNode.SelectSingleNode("//input[@id='txtrid']") == null
                                ? rootNode.SelectSingleNode("//input[@id='CompanyInfo_txtrid']")
                                : rootNode.SelectSingleNode("//input[@id='txtrid']")).Attributes["value"].Value;
                                    var __EVENTTARGET = string.Format("wucXZXKXX$TurnPageBar1$lbtnNextPage");
                                    var __VIEWSTATE = rootNode.SelectSingleNode("//input[@id='__VIEWSTATE']").Attributes["value"].Value;
                                    var __VIEWSTATEGENERATOR = rootNode.SelectSingleNode("//input[@id='__VIEWSTATEGENERATOR']").Attributes["value"].Value;
                                    var __EVENTVALIDATION = rootNode.SelectSingleNode("//input[@id='__EVENTVALIDATION']").Attributes["value"].Value;

                                    request.AddOrUpdateRequestParameter("ScriptManager1", ScriptManager1);
                                    request.AddOrUpdateRequestParameter("txtrid", txtrid);
                                    request.AddOrUpdateRequestParameter("__EVENTTARGET", __EVENTTARGET);
                                    request.AddOrUpdateRequestParameter("__VIEWSTATE", __VIEWSTATE);
                                    request.AddOrUpdateRequestParameter("__VIEWSTATEGENERATOR", __VIEWSTATEGENERATOR);
                                    request.AddOrUpdateRequestParameter("__EVENTVALIDATION", __EVENTVALIDATION);
                                    for (int i = 2; i <= pages; i++)
                                    {


                                        var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("stock_change"));
                                        if (responseList != null && responseList.Any())
                                        {
                                            HtmlDocument hd = new HtmlDocument();
                                            hd.LoadHtml(responseList.First().Data);
                                            var rt = hd.DocumentNode;
                                            var inner_table = rt.SelectSingleNode("//div[@class='item_box']/table");
                                            this.LoadAndParseLicenceContent_SZ(inner_table);

                                            var inner_start = rt.OuterHtml.IndexOf("__VIEWSTATE|");
                                            var inner_end = rt.OuterHtml.IndexOf("|0|asyncPostBackControlIDs");
                                            var temp = rt.OuterHtml.Substring(inner_start + "__VIEWSTATE|".Length, inner_end - inner_start - "__VIEWSTATE|".Length);
                                            var arr = temp.Split('|');
                                            if (arr != null && arr.Any())
                                            {
                                                __VIEWSTATE = arr.First();
                                                __EVENTVALIDATION = arr.Last();
                                                request.AddOrUpdateRequestParameter("__VIEWSTATE", __VIEWSTATE);
                                                request.AddOrUpdateRequestParameter("__EVENTVALIDATION", __EVENTVALIDATION);
                                            }
                                        }
                                    }

                                }
                            }
                        }
                        break;
                    }
                }
            }
        }
        #endregion

        #region 解析深圳行政许可信息内容
        /// <summary>
        /// 解析深圳行政许可信息内容
        /// </summary>
        /// <param name="table"></param>
        void LoadAndParseLicenceContent_SZ(HtmlNode table)
        {
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any() && trs.Count > 1)
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Any() && tds.Count == 8)
                        {
                            LicenseInfo lic = new LicenseInfo();
                            lic.seq_no = _enterpriseInfo.licenses.Count + 1;
                            lic.number = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            lic.name = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            lic.start_date = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            lic.end_date = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            lic.department = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            lic.content = tds[6].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            lic.status = tds[7].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            _enterpriseInfo.licenses.Add(lic);
                        }
                    }
                }
            }
        }

        #endregion

        #region 解析深圳行政处罚信息
        /// <summary>
        /// 解析深圳行政处罚信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseAdministrativePunishment_SZ(HtmlNode rootNode)
        {
            var request = this.CreateRequest();
            var divs = rootNode.SelectNodes("//div[@id='BaseInfo']/div");
            if (divs != null && divs.Any())
            {
                foreach (var div in divs)
                {
                    if (div.InnerText.Contains("行政处罚信息"))
                    {
                        var table = div.SelectSingleNode("./div[@class='item_box']/table");
                        this.LoadAndParseAdministrativePunishmentContent_SZ(table);
                        if (table != null)
                        {
                            var pageDiv = table.SelectSingleNode("./following-sibling::div[1]/div[@class='zongji']");
                            var pages = 1;
                            if (pageDiv != null)
                            {

                                var content = pageDiv.InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").TrimEnd(new char[] { '页' });
                                var index = content.LastIndexOf("共");
                                pages = int.Parse(content.Substring(index + 1));
                                if (pages > 1)
                                {
                                    var ScriptManager1 = string.Format("UpdatePanel15|wucXZCFXX$TurnPageBar1$lbtnNextPage");
                                    var txtrid = (rootNode.SelectSingleNode("//input[@id='txtrid']") == null
                                ? rootNode.SelectSingleNode("//input[@id='CompanyInfo_txtrid']")
                                : rootNode.SelectSingleNode("//input[@id='txtrid']")).Attributes["value"].Value;
                                    var __EVENTTARGET = string.Format("wucXZCFXX$TurnPageBar1$lbtnNextPage");
                                    var __VIEWSTATE = rootNode.SelectSingleNode("//input[@id='__VIEWSTATE']").Attributes["value"].Value;
                                    var __VIEWSTATEGENERATOR = rootNode.SelectSingleNode("//input[@id='__VIEWSTATEGENERATOR']").Attributes["value"].Value;
                                    var __EVENTVALIDATION = rootNode.SelectSingleNode("//input[@id='__EVENTVALIDATION']").Attributes["value"].Value;

                                    request.AddOrUpdateRequestParameter("ScriptManager1", ScriptManager1);
                                    request.AddOrUpdateRequestParameter("txtrid", txtrid);
                                    request.AddOrUpdateRequestParameter("__EVENTTARGET", __EVENTTARGET);
                                    request.AddOrUpdateRequestParameter("__VIEWSTATE", __VIEWSTATE);
                                    request.AddOrUpdateRequestParameter("__VIEWSTATEGENERATOR", __VIEWSTATEGENERATOR);
                                    request.AddOrUpdateRequestParameter("__EVENTVALIDATION", __EVENTVALIDATION);
                                    for (int i = 2; i <= pages; i++)
                                    {


                                        var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("stock_change"));
                                        if (responseList != null && responseList.Any())
                                        {
                                            HtmlDocument hd = new HtmlDocument();
                                            hd.LoadHtml(responseList.First().Data);
                                            var rt = hd.DocumentNode;
                                            var inner_table = rt.SelectSingleNode("//div[@class='item_box']/table");
                                            this.LoadAndParseAdministrativePunishmentContent_SZ(inner_table);

                                            var inner_start = rt.OuterHtml.IndexOf("__VIEWSTATE|");
                                            var inner_end = rt.OuterHtml.IndexOf("|0|asyncPostBackControlIDs");
                                            var temp = rt.OuterHtml.Substring(inner_start + "__VIEWSTATE|".Length, inner_end - inner_start - "__VIEWSTATE|".Length);
                                            var arr = temp.Split('|');
                                            if (arr != null && arr.Any())
                                            {
                                                __VIEWSTATE = arr.First();
                                                __EVENTVALIDATION = arr.Last();
                                                request.AddOrUpdateRequestParameter("__VIEWSTATE", __VIEWSTATE);
                                                request.AddOrUpdateRequestParameter("__EVENTVALIDATION", __EVENTVALIDATION);
                                            }
                                        }
                                    }

                                }
                            }
                        }
                        break;
                    }
                }
            }
        }
        #endregion

        #region 解析深圳行政处罚内容
        /// <summary>
        /// 解析深圳行政处罚内容
        /// </summary>
        /// <param name="table"></param>
        void LoadAndParseAdministrativePunishmentContent_SZ(HtmlNode table)
        {
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any() && trs.Count > 1)
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Any() && tds.Count == 8)
                        {
                            AdministrativePunishment ap = new AdministrativePunishment();
                            ap.seq_no = _enterpriseInfo.administrative_punishments.Count + 1;
                            ap.name = _enterpriseInfo.name;
                            ap.reg_no = _enterpriseInfo.reg_no;
                            ap.number = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            ap.illegal_type = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            ap.content = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            ap.department = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            ap.date = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            ap.public_date = tds[6].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            ap.remark = tds.Last().InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            _enterpriseInfo.administrative_punishments.Add(ap);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析经营异常信息
        /// <summary>
        /// 解析经营异常信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseAbnormal_SZ(HtmlNode rootNode)
        {
            var table = rootNode.SelectSingleNode("//div[@class='main_tabs_box']/div[@class='swiper-container']/div/div[@class='swiper-slide']/div[@id='JYYCMLXX']/div/div/div/table");
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                foreach (var tr in trs)
                {
                    var tds = tr.SelectNodes("./td");
                    if (tds != null && tds.Count == 7)
                    {
                        AbnormalInfo abnormal = new AbnormalInfo();
                        abnormal.name = _enterpriseInfo.name;
                        abnormal.reg_no = _enterpriseInfo.reg_no;
                        abnormal.province = _enterpriseInfo.province;
                        abnormal.in_reason = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        abnormal.in_date = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        abnormal.department = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        abnormal.out_reason = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        abnormal.out_date = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        _abnormals.Add(abnormal);
                    }
                }
                
            }
        }
        #endregion

        #region 解析深圳年报
        /// <summary>
        /// 解析深圳年报
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseReport_SZ(HtmlNode rootNode)
        {
            var divs = rootNode.SelectNodes("//div[@id='BaseInfo']/div");
            if (divs != null && divs.Any())
            {
                foreach (var div in divs)
                {
                    if (div.InnerText.Contains("企业年报信息"))
                    {
                        var table = div.SelectSingleNode("./table");
                        var trs = table.SelectNodes("./tr");
                        if (trs != null && trs.Any())
                        {
                            trs.Remove(0);
                            try
                            {
                                Parallel.ForEach(trs, new ParallelOptions { MaxDegreeOfParallelism = 1 }, tr => this.LoadAndParseReport_SZ_Parallel(tr));
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("年报信息获取失败，" + ex);
                                _enterpriseInfo.reports.Clear();
                            }
                            
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析加载年报-并行
        void LoadAndParseReport_SZ_Parallel(HtmlNode tr)
        {
            var tds = tr.SelectNodes("./td");
            if (tds != null && tds.Count == 4)
            {
                Report report = new Report();
                report.report_name = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                report.report_year = report.report_name.Substring(0, 4);
                report.report_date = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                var aNode = tds.Last().SelectSingleNode("./a");
                if (aNode != null)
                {
                    var href = aNode.Attributes.Contains("href") ? aNode.Attributes["href"].Value : string.Empty;
                    var url = string.Format("https://www.szcredit.org.cn/GJQYCredit/GSZJGSPTS/{0}", href);
                    if (!string.IsNullOrWhiteSpace(href))
                    {
                        string html = string.Empty;
                        if (report.report_year == "2013")
                        {
                            //if (href.Replace(" ", "").Length > 40)
                            //{
                            //    request.ResponseEncoding = "gb2312";
                            //    html = request.HttpGet(url, string.Empty);
                            //    this.LoadAndParseReportDetailShenZhen2013New(html, report);
                            //}
                            //else
                            //{
                            //    request.ResponseEncoding = "utf-8";
                            //    html = request.HttpGet(url, string.Empty);
                            //    this.LoadAndParseReportDetail2013(html, report);
                            //}

                        }
                        else
                        {
                            request.ResponseEncoding = "gb2312";
                            html = request.HttpGet(url, string.Empty);
                            this.LoadAndParseReportDetail_SZ(html, report);

                        }
                    }
                }
                _enterpriseInfo.reports.Add(report);
            }
        }
        #endregion

        #region 解析深圳年报信息
        /// <summary>
        /// 解析深圳年报信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        void LoadAndParseReportDetail_SZ(string responseData,Report report)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            HtmlNode.ElementsFlags.Remove("form");
            HtmlNode.ElementsFlags.Remove("input");
            this.LoadAndParseReportBasic_SZ(rootNode, report);
            this.LoadAndParseReportWebsite_SZ(rootNode, report);
            this.LoadAndParseReportPartner_SZ(rootNode, report);
            this.LoadAndParseReportInvest_SZ(rootNode, report);
            this.LoadAndParseQYZC_SZ(rootNode, report);
            this.LoadAndParseReportGuarantee_SZ(rootNode,report);
            this.LoadAndParseReportStockChange_SZ(rootNode,report);
            this.LoadAndParseReportUpdateRecord_SZ(rootNode,report);
        }
        #endregion

        #region 解析加载深圳年报基本信息
        /// <summary>
        /// 解析加载深圳年报基本信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseReportBasic_SZ(HtmlNode rootNode,Report report)
        {
            var lis = rootNode.SelectNodes("//div[@id='infoPanel']/div[@class='item_box']/div[@class='infor_ul']/ul/li");
            if (lis != null && lis.Any())
            {
                foreach (var li in lis)
                {
                    var spans = li.SelectNodes("./span");
                    if (spans != null && spans.Count == 2)
                    {
                        var title = spans.First().InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").TrimEnd(new char[] { '：' });
                        var val = spans.Last().InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Replace("&nbsp;", "").Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                        switch (title)
                        {
                            case "注册号":
                            case "营业执照注册号":
                                report.reg_no = val;
                                break;
                            case "统一社会信用代码":
                                report.credit_no = val;
                                break;
                            case "注册号/统一社会信用代码":
                            case "统一社会信用代码/注册号":
                                if (val.Length == 18)
                                    report.credit_no = val;
                                else
                                    report.reg_no = val;
                                break;
                            case "企业名称":
                            case "名称":
                            case "经营者":
                                report.name = val;
                                break;
                            case "经营者姓名":
                            case "法人代表":
                                report.oper_name = val;
                                break;
                            case "企业联系电话":
                            case "联系电话":
                                report.telephone = val;
                                break;
                            case "企业通信地址":
                                report.address = val;
                                break;
                            case "邮政编码":
                                report.zip_code = val;
                                break;
                            case "企业电子邮箱":
                            case "电子邮箱":
                                report.email = val;
                                break;
                            case "企业是否有投资信息或购买其他公司股权":
                            case "企业是否有对外投资设立企业信息":
                            case "是否有投资信息或购买其他公司股权":
                                report.if_invest = val;
                                break;
                            case "是否有网站或网店":
                            case "是否有网站或网点":
                                report.if_website = val;
                                break;
                            case "企业经营状态":
                                report.status = val;
                                break;
                            case "从业人数":
                                report.collegues_num = val;
                                break;
                            case "有限责任公司本年度是否发生股东股权转让":
                                report.if_equity = val;
                                break;
                            case "是否有对外提供担保信息":
                                report.if_external_guarantee = val;
                                break;
                            default:
                                break;
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析加载深圳年报网站信息
        /// <summary>
        /// 解析加载深圳年报网站信息
        /// </summary>
        /// <param name="rootNode"></param>
        /// <param name="report"></param>
        void LoadAndParseReportWebsite_SZ(HtmlNode rootNode, Report report)
        {
            var lis = rootNode.SelectNodes("//div[@id='WZHWDXX2']/div[@class='item_box']/div[@class='web_ul']/ul/li");
            if (lis != null && lis.Any())
            {
                foreach (var li in lis)
                {
                    var h3 = li.SelectSingleNode("./h3");
                    var ps = li.SelectNodes("./p");
                    WebsiteItem website = new WebsiteItem();
                    website.seq_no = report.websites.Count + 1;
                    website.web_name = h3.InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                    if (ps != null && ps.Count == 2)
                    {
                        website.web_type = ps.First().InnerText.Replace("·", "").Replace("类型：","").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        website.web_url = ps.Last().InnerText.Replace(".网址：", "").Replace("·网址：", "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Replace("·网址：", "");
                    }
                    
                    report.websites.Add(website);
                }
                
            }
        }
        #endregion

        #region 解析加载深圳年报股东及出资信息
        /// <summary>
        /// 解析加载深圳年报股东及出资信息
        /// </summary>
        /// <param name="rootNode"></param>
        /// <param name="report"></param>
        void LoadAndParseReportPartner_SZ(HtmlNode rootNode, Report report)
        {
            var table = rootNode.SelectSingleNode("//div[@id='GDJQTZ3']/div[@class='item_box']/table");
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Count == 8)
                        {
                            Partner partner = new Partner();
                            partner.seq_no = report.partners.Count + 1;
                            partner.stock_name = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            ShouldCapiItem sci = new ShouldCapiItem();
                            sci.shoud_capi = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            sci.should_capi_date = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            sci.invest_type = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            partner.should_capi_items.Add(sci);

                            RealCapiItem rci = new RealCapiItem();
                            rci.real_capi = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            rci.real_capi_date = tds[6].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            rci.invest_type = tds[7].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            partner.real_capi_items.Add(rci);
                            report.partners.Add(partner);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析加载深圳年报对外投资信息
        /// <summary>
        /// 解析加载深圳年报对外投资信息
        /// </summary>
        /// <param name="rootNode"></param>
        /// <param name="report"></param>
        void LoadAndParseReportInvest_SZ(HtmlNode rootNode, Report report)
        {
            var lis = rootNode.SelectNodes("//div[@id='DWTZXX4']/div[@class='item_box']/div[@class='web_ul']/ul/li");
            if (lis != null && lis.Any())
            {
                foreach (var li in lis)
                {
                    var h4 = li.SelectSingleNode("./h4");
                    var spans = li.SelectNodes("./span");
                    InvestItem invest = new InvestItem();
                    invest.seq_no = report.invest_items.Count + 1;
                    invest.invest_name = h4.InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                    if (spans != null && spans.Any())
                    {
                        invest.invest_reg_no = spans.Last().InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                    }

                    report.invest_items.Add(invest);
                }

            }
        }
        #endregion

        #region 解析加载深圳企业资产状况信息
        /// <summary>
        /// 解析企业资产状况信息
        /// </summary>
        /// <param name="rootNode"></param>
        /// <param name="report"></param>
        void LoadAndParseQYZC_SZ(HtmlNode rootNode, Report report)
        {
            var table = rootNode.SelectSingleNode("//div[@id='QYZCZKXX4']/div[@class='item_box']/table");
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    foreach (var tr in trs)
                    {
                        var ths = tr.SelectNodes("./th");
                        var tds = tr.SelectNodes("./td");
                        for (int i = 0; i < ths.Count; i++)
                        {
                            var title = ths[i].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Trim();
                            switch (title)
                            {
                                case "资产总额":
                                    report.total_equity = tds[i].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Trim();
                                    break;
                                case "负债总额":
                                case "金融贷款":
                                    report.debit_amount = tds[i].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Trim();
                                    break;
                                case "销售总额":
                                case "营业总收入":
                                case "销售额或营业收入":
                                    report.sale_income = tds[i].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Trim();
                                    break;
                                case "其中：主营业务收入":
                                    report.serv_fare_income = tds[i].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Trim();
                                    break;
                                case "利润总额":
                                case "盈余总额":
                                    report.profit_total = tds[i].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Trim();
                                    break;
                                case "净利润":
                                    report.net_amount = tds[i].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Trim();
                                    break;
                                case "纳税总额":
                                case "纳税金额":
                                    report.tax_total = tds[i].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Trim();
                                    break;
                                case "所有者权益合计":
                                case "获得政府扶持资金、补助":
                                    report.profit_reta = tds[i].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Trim();
                                    break;
                                default:
                                    break;

                            }
                        } 
                    }
                }
            }
            
        }
        #endregion

        #region 解析加载深圳对外提供保证担保信息
        void LoadAndParseReportGuarantee_SZ(HtmlNode rootNode, Report report)
        {
            var table = rootNode.SelectSingleNode("//div[@class='main_tabs_box']/div[@class='tabs_box']/div/div[@id='DWTGBZDBXX6']/div[@class='item_box']/table");
            var trs = table.SelectNodes("./tr");
                
            if (trs != null && trs.Any())
            {
                trs.Remove(0);
                foreach (var tr in trs)
                {
                    var tds = tr.SelectNodes("./td");
                    if (tds != null && tds.Any() && tds.Count == 8)
                    {
                        ExternalGuarantee eg = new ExternalGuarantee();
                        eg.seq_no = report.external_guarantees.Count + 1;
                        eg.creditor = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        eg.debtor = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        eg.type = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        eg.amount = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        eg.period = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        eg.guarantee_time = tds[6].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        eg.guarantee_type = tds[7].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        report.external_guarantees.Add(eg);
                    }
                }
            }
        }
        #endregion

        #region 解析深圳年报股权变更信息--年报
        /// <summary>
        /// 股权变更信息--年报
        /// </summary>
        /// <param name="table"></param>
        /// <param name="report"></param>
        void LoadAndParseReportStockChange_SZ(HtmlNode rootNode, Report report)
        {
            var table = rootNode.SelectSingleNode("//div[@class='main_tabs_box']/div[@class='tabs_box']/div/div[@id='GQBGXX']/div[@class='item_box']/table");
            if (table == null) return;
            var trs = table.SelectNodes("./tr");
            if (trs != null && trs.Any())
            {
                trs.Remove(0);
                foreach (var tr in trs)
                {
                    var tds = tr.SelectNodes("./td");
                    if (tds != null && tds.Any() && tds.Count >= 5)
                    {
                        StockChangeItem sci = new StockChangeItem();
                        sci.seq_no = report.stock_changes.Count + 1;
                        sci.name = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        sci.before_percent = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        sci.after_percent = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        sci.change_date = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", ""); ;
                        report.stock_changes.Add(sci);
                    }
                }
            }
        }
        #endregion

        #region 解析深圳年报修改信息--年报
        /// <summary>
        /// 解析广东修改信息--年报
        /// </summary>
        /// <param name="table"></param>
        /// <param name="report"></param>
        void LoadAndParseReportUpdateRecord_SZ(HtmlNode rootNode, Report report)
        {
            var table = rootNode.SelectSingleNode("//div[@class='main_tabs_box']/div[@class='tabs_box']/div/div[@id='XGJL']/div[@class='item_box']/table");
            if (table == null) return;
            var trs = table.SelectNodes("./tr");
            if (trs != null && trs.Any())
            {
                trs.Remove(0);
                foreach (var tr in trs)
                {
                    var tds = tr.SelectNodes("./td");
                    if (tds != null && tds.Any() && tds.Count == 5)
                    {
                        UpdateRecord ur = new UpdateRecord();
                        ur.seq_no = report.update_records.Count + 1;
                        ur.update_item = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        ur.before_update = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        ur.after_update = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        ur.update_date = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        report.update_records.Add(ur);
                    }
                }
            }
        }
        #endregion

        #region 加载解析2013年年报详细信息
        /// <summary>
        /// 深圳市：加载解析2013年年报详细信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportDetail2013(string responseData, Report report)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            // 企业基本信息
            HtmlNode table = rootNode.SelectSingleNode("//table[@id='tabNameWrite']");
            HtmlNodeCollection trList = table.SelectNodes("./tr");
            foreach (HtmlNode rowNode in trList)
            {
                HtmlNodeCollection tdList = rowNode.SelectNodes("./td");

                if (tdList != null && tdList.Count > 1)
                {
                    for (int i = 0; i < tdList.Count; i += 2)
                    {
                        if (tdList[i].InnerText.Contains("名称") || tdList[i].InnerText.Contains("名 称"))
                        {
                            report.name = tdList[i + 1].InnerText.Trim().Replace("&nbsp;", "").Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                        }
                        else if (tdList[i].InnerText.Contains("住所") || tdList[i].InnerText.Contains("住 所"))
                        {
                            report.address = tdList[i + 1].InnerText.Trim().Replace("&nbsp;", "");
                        }
                        else if (tdList[i].InnerText.Contains("经营场所"))
                        {
                            if (tdList[i + 1].InnerText.Trim().Replace("&nbsp;", "") != "")
                            {
                                report.address = tdList[i + 1].InnerText.Trim().Replace("&nbsp;", "");
                            }
                        }
                        else if (tdList[i].InnerText.Contains("法定代表人"))
                        {
                            report.oper_name = tdList[i + 1].InnerText.Trim().Replace("&nbsp;", "");
                        }
                        else if (tdList[i].InnerText.Contains("出资总额"))
                        {
                            report.reg_capi = tdList[i + 1].InnerText.Trim().Replace("&nbsp;", "");
                        }
                        else if (tdList[i].InnerText.Contains("经营期限"))
                        {
                        }
                        else if (tdList[i].InnerText.Contains("从业人员"))
                        {
                            report.collegues_num = tdList[i + 1].InnerText.Trim().Replace("&nbsp;", "");
                        }
                        else if (tdList[i].InnerText.Contains("投资人姓名"))
                        {
                            report.collegues_num = tdList[i + 1].InnerText.Trim().Replace("&nbsp;", "");
                        }
                    }
                }
            }

            // 解析需要的参数
            string __EVENTARGUMENT = "";
            string __VIEWSTATE = "";
            string __VIEWSTATEGENERATOR = "";
            string __EVENTVALIDATION = "";
            __EVENTARGUMENT = rootNode.SelectSingleNode("//input[@id='__EVENTARGUMENT']").Attributes["value"].Value;
            __VIEWSTATE = rootNode.SelectSingleNode("//input[@id='__VIEWSTATE']").Attributes["value"].Value;
            // __VIEWSTATEGENERATOR = string.Empty;// rootNode.SelectSingleNode("//input[@id='__VIEWSTATEGENERATOR']").Attributes["value"].Value;
            __EVENTVALIDATION = rootNode.SelectSingleNode("//input[@id='__EVENTVALIDATION']").Attributes["value"].Value;

            string parameter = "";
            string result = "";
            string urlParameter = rootNode.SelectSingleNode("//form").Attributes["action"].Value.Replace("&amp;", "&");
            string url = "http://app02.szaic.gov.cn/NB.WebUI/WebPages/Publicity/" + urlParameter;
            for (int i = 1; i <= 4; i++)
            {
                parameter = string.Format("{0}={1}", HttpUtility.UrlEncode("ctl00$ContentPlaceHolder1$smObj"), HttpUtility.UrlEncode("ctl00$ContentPlaceHolder1$UpdatePanel1|ctl00$ContentPlaceHolder1$lbtnTag"))
                    + i + "&__EVENTTARGET=" + HttpUtility.UrlEncode("ctl00$ContentPlaceHolder1$lbtnTag")
                    + i + "&__EVENTARGUMENT=" + HttpUtility.UrlEncode(__EVENTARGUMENT)
                    + "&__VIEWSTATE=" + HttpUtility.UrlEncode(__VIEWSTATE)
                    // + "&__VIEWSTATEGENERATOR=" + HttpUtility.UrlEncode(__VIEWSTATEGENERATOR)
                    + "&__EVENTVALIDATION=" + HttpUtility.UrlEncode(__EVENTVALIDATION)
                    + "&__ASYNCPOST=true&";

                request.ResponseEncoding = "utf-8";
                result = request.HttpPost(url, parameter);
                document.LoadHtml(result);
                rootNode = document.DocumentNode;
                HtmlNodeCollection tables = rootNode.SelectNodes("//table[@id='tabNameWrite']");

                MatterRecord mr = new MatterRecord();//备案信息分布在不同的table
                foreach (HtmlNode tableNode in tables)
                {
                    if (tableNode.InnerText.Contains("对外投资情况"))
                    {
                        #region 对外投资情况
                        List<InvestItem> investList = new List<InvestItem>();
                        HtmlNodeCollection trs = tableNode.SelectNodes("./tr");
                        if (trs != null && trs.Count >= 3)
                        {
                            for (int trIndex = 2; trIndex < trs.Count; trIndex++)
                            {
                                HtmlNodeCollection tds = trs[trIndex].SelectNodes("./td");
                                InvestItem item = new InvestItem();
                                item.seq_no = investList.Count + 1;
                                item.invest_name = tds[0].InnerText.Trim();
                                item.invest_reg_no = tds[1].InnerText.Trim();
                                item.invest_capi = string.IsNullOrEmpty(tds[2].InnerText.Trim()) ? "" : tds[2].InnerText.Trim();
                                item.invest_percent = tds[3].InnerText.Trim();
                                investList.Add(item);
                            }
                        }
                        report.invest_items = investList;
                        #endregion
                    }
                    else if (tableNode.InnerText.Contains("章程信息"))
                    {
                        HtmlNodeCollection trs = tableNode.SelectNodes("./tr");
                        if (trs != null && trs.Count == 3)
                        {

                            HtmlNodeCollection tds1 = trs[1].SelectNodes("./td");
                            HtmlNodeCollection tds2 = trs[2].SelectNodes("./td");
                            mr.is_same = tds1[1].InnerText.Trim();
                            mr.differ_content = tds2[1].InnerText.Trim();
                        }
                    }
                    else if (tableNode.InnerText.Contains("经营范围"))
                    {
                        HtmlNodeCollection trs = tableNode.SelectNodes("./tr");
                        if (trs != null && trs.Count == 3)
                        {
                            HtmlNodeCollection tds1 = trs[1].SelectNodes("./td");
                            HtmlNodeCollection tds2 = trs[2].SelectNodes("./td");
                            mr.general_scope = tds1[1].InnerText.Trim();
                            mr.permit_scope = tds2[1].InnerText.Trim();
                        }
                    }
                    else if (tableNode.InnerText.Contains("高级管理人员姓名"))
                    {
                        HtmlNodeCollection trs = tableNode.SelectNodes("./tr");
                        List<Employee> employeeLst = new List<Employee>();
                        Employee employee = null;
                        foreach (var tr in trs)
                        {
                            employee = new Employee();
                            HtmlNodeCollection tds = tr.SelectNodes("./td");
                            if (tds != null && tds.Count > 1)
                            {
                                if (tds[0].InnerText.Trim() == "名称") continue;
                                employee.seq_no = employeeLst.Count + 1;
                                employee.name = tds[0].InnerText.Trim();
                                employee.job_title = tds[1].InnerText.Trim();
                                employeeLst.Add(employee);
                            }
                        }
                        mr.employees = employeeLst;
                        report.matter_record.Add(mr);
                    }
                    else if (tableNode.InnerText.Contains("出资人姓名（名 称）及出资额"))
                    {
                        #region 注册资本实缴情况
                        List<Partner> partnerList = new List<Partner>();
                        HtmlNodeCollection rows = tables[0].SelectNodes("./tr");
                        if (rows != null && rows.Count >= 3)
                        {
                            for (int trIndex = 2; trIndex < rows.Count; trIndex++)
                            {
                                HtmlNodeCollection tds = rows[trIndex].SelectNodes("./td");
                                if (tds != null && tds.Count > 3)
                                {
                                    Partner item = new Partner();
                                    item.seq_no = trIndex - 1;
                                    item.stock_name = tds[0].InnerText.Trim();
                                    item.stock_percent = tds[2].InnerText.Trim();
                                    item.should_capi_items = new List<ShouldCapiItem>();
                                    ShouldCapiItem sItem = new ShouldCapiItem();
                                    sItem.shoud_capi = tds[1].InnerText.Trim();
                                    sItem.invest_type = "";
                                    sItem.should_capi_date = "";
                                    item.should_capi_items.Add(sItem);
                                    item.real_capi_items = new List<RealCapiItem>();
                                    RealCapiItem rItem = new RealCapiItem();
                                    rItem.real_capi = tds[3].InnerText.Trim();
                                    rItem.invest_type = "";
                                    rItem.real_capi_date = "";
                                    item.real_capi_items.Add(rItem);

                                    partnerList.Add(item);
                                }
                            }
                        }
                        report.partners = partnerList;
                        #endregion
                    }
                    else if (tableNode.InnerText.Contains("投资者名称"))
                    {
                        #region 注册资本实缴情况
                        List<RegistCapiInfo> rc = new List<RegistCapiInfo>();
                        HtmlNodeCollection rows = tables[0].SelectNodes("./tr");
                        if (rows != null && rows.Count > 2)
                        {
                            for (int trIndex = 2; trIndex < rows.Count; trIndex++)
                            {
                                HtmlNodeCollection tds = rows[trIndex].SelectNodes("./td");
                                if (tds != null && tds.Count == 3)
                                {
                                    RegistCapiInfo item = new RegistCapiInfo();
                                    item.seq_no = rc.Count + 1;
                                    item.name = tds[0].InnerText.Trim();
                                    item.user_type = tds[1].InnerText.Trim();
                                    item.state = tds[2].InnerText.Trim();
                                    rc.Add(item);
                                }
                            }
                        }
                        report.regist_capi_info = rc;
                        #endregion
                    }
                    else if (tableNode.InnerText.Contains("出资情况"))
                    {
                        InvestSituation item = GetInvestSituation(tables);
                        report.invest_situation.Add(item);
                    }
                    else if (tableNode.InnerText.Contains("分支机构登记情况"))
                    {
                        var branchList = GetSZReport2013Branchs(tableNode);
                        report.branch = branchList;
                    }
                }
            }
        }
        #endregion

        #region 加载解析2013年年报详细
        /// <summary>
        /// 加载解析2013年年报详细
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportDetailShenZhen2013New(string responseData, Report report)
        {
            if (string.IsNullOrWhiteSpace(responseData))
            {
                return;
            }
            responseData = Regex.Replace(responseData, @"(<tbody)|(</tbody>)", "");
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            HtmlNodeCollection tables = rootNode.SelectNodes("//table[@class='detailsList']");
            if (tables != null)
            {
                foreach (HtmlNode table in tables)
                {
                    string title = table.SelectNodes("./tr/th")[0].InnerText.Trim();
                    if (title.EndsWith("红色为修改过的信息项"))
                    {
                        // 企业基本信息
                        HtmlNodeCollection trList = table.SelectNodes("./tr");
                        foreach (HtmlNode rowNode in trList)
                        {
                            HtmlNodeCollection thList = rowNode.SelectNodes("./th");
                            HtmlNodeCollection tdList = rowNode.SelectNodes("./td");

                            if (thList != null && tdList != null && thList.Count == tdList.Count)
                            {
                                for (int i = 0; i < thList.Count; i++)
                                {
                                    switch (thList[i].InnerText.Trim())
                                    {
                                        case "注册号":
                                        case "营业执照注册号":
                                            report.reg_no = tdList[i].InnerText.Trim().Replace("&nbsp;", "");
                                            break;
                                        case "统一社会信用代码":
                                            report.credit_no = tdList[i].InnerText.Trim().Replace("&nbsp;", "");
                                            break;
                                        case "注册号/统一社会信用代码":
                                        case "统一社会信用代码/注册号":
                                            if (tdList[i].InnerText.Trim().Replace("&nbsp;", "").Length == 18)
                                                report.credit_no = tdList[i].InnerText.Trim().Replace("&nbsp;", "");
                                            else
                                                report.reg_no = tdList[i].InnerText.Trim().Replace("&nbsp;", "");
                                            break;
                                        case "企业名称":
                                        case "名称":
                                            report.name = tdList[i].InnerText.Trim().Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                                            break;
                                        case "企业联系电话":
                                        case "联系电话":
                                            report.telephone = tdList[i].InnerText.Trim();
                                            break;
                                        case "住所":
                                        case "企业通信地址":
                                            report.address = tdList[i].InnerText.Trim();
                                            break;
                                        case "邮政编码":
                                            report.zip_code = tdList[i].InnerText.Trim();
                                            break;
                                        case "电子邮箱":
                                        case "企业电子邮箱":
                                            report.email = tdList[i].InnerText.Trim();
                                            break;
                                        case "企业是否有投资信息或购买其他公司股权":
                                        case "企业是否有对外投资设立企业信息":
                                        case "是否有投资信息或购买其他公司股权":
                                            report.if_invest = tdList[i].InnerText.Trim();
                                            break;
                                        case "是否有网站或网店":
                                        case "是否有网站或网点":
                                            report.if_website = tdList[i].InnerText.Trim();
                                            break;
                                        case "企业经营状态":
                                            report.status = tdList[i].InnerText.Trim();
                                            break;
                                        case "从业人数":
                                            report.collegues_num = tdList[i].InnerText.Trim();
                                            break;
                                        case "有限责任公司本年度是否发生股东股权转让":
                                            report.if_equity = tdList[i].InnerText.Trim();
                                            break;
                                        case "经营者姓名":
                                        case "法人代表":
                                            report.oper_name = tdList[i].InnerText.Trim();
                                            break;
                                        case "资金数额":
                                            report.reg_capi = tdList[i].InnerText.Trim();
                                            break;
                                        default:
                                            break;
                                    }
                                }
                            }
                        }
                    }
                    else if (title == "网站或网店信息")
                    {
                        var trList = table.SelectNodes("./tr");
                        trList.RemoveAt(0);
                        trList.RemoveAt(0);
                        List<WebsiteItem> websiteList = new List<WebsiteItem>();
                        foreach (HtmlNode rowNode in trList)
                        {
                            HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                            if (tdList != null && tdList.Count > 2 && tdList[0].InnerText.Trim() != "")
                            {
                                WebsiteItem item = new WebsiteItem();

                                item.seq_no = report.websites.Count() + 1;
                                item.web_type = tdList[0].InnerText;
                                item.web_name = tdList[1].InnerText;
                                item.web_url = tdList[2].InnerText;

                                websiteList.Add(item);
                            }
                        }
                        report.websites = websiteList;
                    }
                    else if (title == "股东及出资信息")
                    {
                        var trList = table.SelectNodes("./tr");
                        trList.RemoveAt(0);
                        trList.RemoveAt(0);
                        List<Partner> partnerList = new List<Partner>();
                        int j = 1;
                        foreach (HtmlNode rowNode in trList)
                        {
                            HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                            if (tdList != null && tdList.Count > 5 && tdList[0].InnerText.Trim() != "")
                            {
                                Partner item = new Partner();

                                item.seq_no = j++;
                                item.stock_name = tdList[0].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                item.stock_type = "";
                                item.identify_no = "";
                                item.identify_type = "";
                                item.stock_percent = "";
                                item.ex_id = "";
                                item.real_capi_items = new List<RealCapiItem>();
                                item.should_capi_items = new List<ShouldCapiItem>();

                                ShouldCapiItem sItem = new ShouldCapiItem();
                                var sCapi = tdList[1].InnerText.Trim();
                                sItem.shoud_capi = string.IsNullOrEmpty(sCapi) ? "" : sCapi;
                                sItem.should_capi_date = tdList[2].InnerText.Trim();
                                sItem.invest_type = tdList[3].InnerText.Trim();
                                item.should_capi_items.Add(sItem);

                                RealCapiItem rItem = new RealCapiItem();
                                var rCapi = tdList[4].InnerText.Trim();
                                rItem.real_capi = string.IsNullOrEmpty(rCapi) ? "" : rCapi;
                                rItem.real_capi_date = tdList[5].InnerText.Trim();
                                rItem.invest_type = tdList[6].InnerText.Trim();
                                item.real_capi_items.Add(rItem);

                                partnerList.Add(item);
                            }
                        }
                        report.partners = partnerList;
                    }
                    else if (title == "对外投资信息" || title == "对外投资情况")
                    {
                        var trList = table.SelectNodes("./tr");
                        trList.RemoveAt(0);
                        trList.RemoveAt(0);
                        List<InvestItem> investList = new List<InvestItem>();
                        int j = 1;
                        foreach (HtmlNode rowNode in trList)
                        {
                            HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                            if (tdList != null && tdList.Count > 1 && tdList[0].InnerText.Trim() != "")
                            {
                                InvestItem item = new InvestItem();

                                item.seq_no = j++;
                                item.invest_name = tdList[0].InnerText;
                                item.invest_reg_no = tdList[1].InnerText;

                                investList.Add(item);
                            }
                        }
                        report.invest_items = investList;
                    }
                    else if (title == "企业资产状况信息" || title == "生产经营情况信息" || title == "资产状况信息")
                    {
                        var trList = table.SelectNodes("./tr");
                        trList.RemoveAt(0);
                        foreach (HtmlNode rowNode in trList)
                        {
                            HtmlNodeCollection thList = rowNode.SelectNodes("./th");
                            HtmlNodeCollection tdList = rowNode.SelectNodes("./td");

                            if (thList != null && tdList != null && thList.Count == tdList.Count)
                            {
                                for (int i = 0; i < thList.Count; i++)
                                {
                                    switch (thList[i].InnerText.Trim())
                                    {
                                        case "资产总额":
                                            report.total_equity = tdList[i].InnerText.Trim();
                                            break;
                                        case "负债总额":
                                            report.debit_amount = tdList[i].InnerText.Trim();
                                            break;
                                        case "销售(营业)收入":
                                        case "销售总额":
                                        case "营业总收入":
                                        case "营业额或营业收入":
                                            report.sale_income = tdList[i].InnerText.Trim();
                                            break;
                                        case "其中：主营业务收入":
                                        case "营业总收入中主营业务收入":
                                            report.serv_fare_income = tdList[i].InnerText.Trim();
                                            break;
                                        case "利润总额":
                                            report.profit_total = tdList[i].InnerText.Trim();
                                            break;
                                        case "净利润":
                                            report.net_amount = tdList[i].InnerText.Trim();
                                            break;
                                        case "纳税总额":
                                            report.tax_total = tdList[i].InnerText.Trim();
                                            break;
                                        case "所有者权益合计":
                                            report.profit_reta = tdList[i].InnerText.Trim();
                                            break;
                                        default:
                                            break;
                                    }
                                }
                            }
                        }
                    }
                    else if (title.Contains("股权变更信息"))
                    {
                        var trList = table.SelectNodes("./tr");
                        trList.RemoveAt(0);
                        trList.RemoveAt(0);
                        foreach (HtmlNode rowNode in trList)
                        {
                            HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                            if (tdList != null && tdList.Count > 2)
                            {
                                StockChangeItem item = new StockChangeItem();
                                item.seq_no = report.stock_changes.Count + 1;
                                item.name = tdList[0].InnerText;
                                item.before_percent = tdList[1].InnerText;
                                item.after_percent = tdList[2].InnerText;
                                item.change_date = tdList[3].InnerText;
                                report.stock_changes.Add(item);
                            }
                        }
                    }
                    else if (title.Contains("对外提供保证担保信息") || title.Contains("对外提供担保情况"))
                    {
                        var trList = table.SelectNodes("./tr");
                        trList.RemoveAt(0);
                        trList.RemoveAt(0);
                        foreach (HtmlNode rowNode in trList)
                        {
                            HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                            if (tdList != null && tdList.Count > 6)
                            {
                                ExternalGuarantee item = new ExternalGuarantee();
                                item.seq_no = report.external_guarantees.Count + 1;
                                item.creditor = tdList[0].InnerText.Trim();
                                item.debtor = tdList[1].InnerText.Trim();
                                item.type = tdList[2].InnerText.Trim();
                                item.amount = tdList[3].InnerText.Trim();
                                item.period = tdList[4].InnerText.Trim();
                                item.guarantee_time = tdList[5].InnerText.Trim();
                                item.guarantee_type = tdList[6].InnerText.Trim();
                                report.external_guarantees.Add(item);
                            }
                        }
                    }
                    else if (title == "修改记录")
                    {
                        var trList = table.SelectNodes("./tr");
                        trList.RemoveAt(0);
                        trList.RemoveAt(0);
                        foreach (HtmlNode rowNode in trList)
                        {
                            HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                            if (tdList != null && tdList.Count > 1)
                            {
                                UpdateRecord item = new UpdateRecord();

                                item.seq_no = report.update_records.Count + 1;
                                item.update_item = tdList[1].InnerText;
                                item.before_update = tdList[2].SelectSingleNode("./span[@id='beforeMore2_2']") == null ? tdList[2].InnerText : tdList[2].SelectSingleNode("./span[@id='beforeMore2_2']").InnerText.Replace("收起更多", "");
                                item.after_update = tdList[3].SelectSingleNode("./span[@id='beforeMore2_2']") == null ? tdList[3].InnerText : tdList[2].SelectSingleNode("./span[@id='beforeMore2_2']").InnerText.Replace("收起更多", "");
                                item.update_date = tdList[4].InnerText;
                                report.update_records.Add(item);
                            }
                        }
                    }
                }
            }
        }

        #endregion

        #region GetInvestSituation
        /// <summary>
        /// 获取出资情况
        /// </summary>
        /// <param name="tables"></param>
        /// <returns></returns>
        private static InvestSituation GetInvestSituation(HtmlNodeCollection tables)
        {
            InvestSituation item = new InvestSituation();
            var table1 = tables[0].SelectSingleNode("//table[@id='Table1']");
            if (table1 != null)
            {
                HtmlNodeCollection rows = table1.SelectNodes("./tr");
                if (rows != null && rows.Count > 4)
                {
                    HtmlNodeCollection tds0 = rows[0].SelectNodes("./td");
                    HtmlNodeCollection tds1 = rows[1].SelectNodes("./td");
                    HtmlNodeCollection tds2 = rows[2].SelectNodes("./td");
                    HtmlNodeCollection tds3 = rows[3].SelectNodes("./td");
                    HtmlNodeCollection tds4 = rows[4].SelectNodes("./td");
                    if (tds0 != null && tds0.Count > 1)
                        item.total_amount = tds0[1].InnerText.Trim().Replace(" ", "").Replace(Environment.NewLine, ":");


                    if (tds1 != null && tds1.Count > 1 && tds2 != null && tds2.Count == 1)
                    {
                        item.regist_china_capi = tds1[1].InnerText.Trim().Replace(" ", "").Replace(Environment.NewLine, ":");
                        item.regist_foreign_capi = tds2[0].InnerText.Trim().Replace(" ", "").Replace(Environment.NewLine, ":");
                    }

                    if (tds3 != null && tds3.Count > 1 && tds4 != null && tds4.Count == 1)
                    {
                        item.paid_china_capi = tds3[1].InnerText.Trim().Replace(" ", "").Replace(Environment.NewLine, ":");
                        item.paid_foreign_capi = tds4[0].InnerText.Trim().Replace(" ", "").Replace(Environment.NewLine, ":");
                    }
                    item.invest_form = new List<InvestmentForm>();

                }
            }
            var table2 = tables[0].SelectSingleNode("//table[@id='Table2']");
            if (table2 != null)
            {
                InvestmentForm invesForm = null;
                List<InvestmentForm> invesFormLst = new List<InvestmentForm>();
                HtmlNodeCollection rows = table2.SelectNodes("./tr");
                if (rows != null && rows.Count == 5)
                {
                    HtmlNodeCollection tds0 = rows[0].SelectNodes("./td");
                    HtmlNodeCollection tds1 = rows[1].SelectNodes("./td");
                    HtmlNodeCollection tds2 = rows[2].SelectNodes("./td");
                    HtmlNodeCollection tds3 = rows[3].SelectNodes("./td");
                    HtmlNodeCollection tds4 = rows[4].SelectNodes("./td");
                    for (int k = 0; k < 5; k++)
                    {
                        invesForm = new InvestmentForm();
                        invesForm.invest_form = tds0[k + 1].InnerText.Trim();
                        invesForm.invest_amount = tds1[k + 1].InnerText.Trim();
                        invesFormLst.Add(invesForm);
                    }
                    item.invest_form = invesFormLst;
                    item.china_should_capi = tds2[2].InnerText.Trim();
                    item.foreign_should_capi = tds3[2].InnerText.Trim();
                    item.china_real_capi = tds2[5].InnerText.Trim();
                    item.foreign_real_capi = tds3[5].InnerText.Trim();
                    item.is_full = tds4[1].InnerText.Trim();
                }
            }
            return item;
        }
        #endregion

        #region GetSZReport2013Branchs
        /// <summary>
        /// GetSZReport2013Branchs
        /// </summary>
        /// <param name="tableNode"></param>
        /// <returns></returns>
        private static List<Branch> GetSZReport2013Branchs(HtmlNode tableNode)
        {
            List<Branch> branchList = new List<Branch>();
            HtmlNodeCollection trs = tableNode.SelectNodes("./tr");
            if (trs != null && trs.Count > 2)
            {
                for (int trIndex = 2; trIndex < trs.Count; trIndex++)
                {
                    HtmlNodeCollection tds = trs[trIndex].SelectNodes("./td");
                    Branch item = new Branch();
                    item.seq_no = branchList.Count + 1;
                    item.name = tds[0].InnerText.Trim();
                    item.reg_no = tds[1].InnerText.Trim();
                    item.belong_org = tds[2].InnerText.Trim();
                    item.oper_name = tds[3].InnerText.Trim();
                    branchList.Add(item);
                }
            }
            return branchList;
        }
        #endregion

        #region ConvertStringToDate
        private string ConvertStringToDate(string timespan)
        {
            try
            {
                DateTime dt = new DateTime(1970, 1, 1, 12, 0, 0);
                var date = dt.AddMilliseconds(double.Parse(timespan));

                return date.ToString("yyyy年MM月dd日");
            }
            catch
            {
                return string.Empty;
            }
        }
        #endregion
    }
}

