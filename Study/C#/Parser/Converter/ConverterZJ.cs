using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Reflection;
using System.Collections;
using System.Xml.Linq;
using System.Globalization;

using Newtonsoft.Json;
using iOubo.iSpider.Model;
using iOubo.iSpider.Common;
using HtmlAgilityPack;
using System.Threading;
using System.Configuration;
using MongoDB.Bson;


namespace iOubo.iSpider.Infrastructure.Parser
{
    public class ConverterZJ : IConverter
    {
        DataRequest _request;
        RequestInfo _requestInfo;
        RequestXml _requestXml;
        EnterpriseInfo _enterpriseInfo = new EnterpriseInfo();
        List<AbnormalInfo> _abnormals = new List<AbnormalInfo>();
        List<CheckupInfo> _checkups = new List<CheckupInfo>();
        string _entTypeCatg = string.Empty;

        public RequestHandler request = new RequestHandler();
        List<string> reportsNeedToLoad = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings.Get("ReportsNeedToLoad"))
            ? new List<string>() : ConfigurationManager.AppSettings.Get("ReportsNeedToLoad").Split(',').ToList();
        public SummaryEntity ProcessRequestAndParse(RequestInfo requestInfo)
        {
            Random ran = new Random();
            this._requestInfo = requestInfo;
            this._request = new DataRequest(requestInfo);
            if (requestInfo.Parameters.ContainsKey("platform") && "LIST_API" == requestInfo.Parameters["platform"])
            {
                this._requestXml = new RequestXml(requestInfo.CurrentPath, requestInfo.Province + "_API");
            }
            else
            {
                this._requestXml = new RequestXml(requestInfo.CurrentPath, requestInfo.Province);
            }
            InitialEnterpriseInfo();
            List<ResponseInfo> responseList = new List<ResponseInfo>();
            List<XElement> requestList = new List<XElement>();
            
            responseList = _request.GetResponseInfo(_requestXml.GetRequestListByName("jiben"));
            if (responseList != null && responseList.Any())
            {
                this.LoadAndParseDengJiJiben(responseList.First().Data);
            }
            if (_entTypeCatg == "11" || _entTypeCatg == "31" || _entTypeCatg == "21" || _entTypeCatg == "13" || _entTypeCatg == "33" || _entTypeCatg == "27")
            { 
                //股东信息
                requestList.AddRange(_requestXml.GetRequestListByName("partner"));
            }
            if (_entTypeCatg == "11" || _entTypeCatg == "31" || _entTypeCatg == "21")
            { 
                //主要人员
                requestList.AddRange(_requestXml.GetRequestListByName("employee"));
            }
            if (_entTypeCatg == "16")
            { 
                //农专成员名册
                requestList.AddRange(_requestXml.GetRequestListByName("employee_nz"));
            }
            if (_entTypeCatg == "11" || _entTypeCatg == "33" || _entTypeCatg == "31" || _entTypeCatg == "21" || _entTypeCatg == "27")
            { 
                //分支机构信息
                requestList.AddRange(_requestXml.GetRequestListByName("branch"));
            }
            if (_entTypeCatg == "50")//个体户
            {
                requestList.AddRange(_requestXml.GetRequestListByName("report_gt"));
                requestList.AddRange(_requestXml.GetRequestListByName("abnormal_gt"));
            }
            else if (_entTypeCatg == "16" || _entTypeCatg == "17")
            {
                requestList.AddRange(_requestXml.GetRequestListByName("report_nz"));
                requestList.AddRange(_requestXml.GetRequestListByName("abnormal"));
            }
            else
            {
                requestList.AddRange(_requestXml.GetRequestListByName("report"));
                requestList.AddRange(_requestXml.GetRequestListByName("abnormal"));
            }
            if (_entTypeCatg == "11" || _entTypeCatg == "21")//司法协助信息、股权出质登记信息
            {
                requestList.AddRange(_requestXml.GetRequestListByName("equity_quality"));
            }
            if (_entTypeCatg != "50" || _entTypeCatg != "16" || _entTypeCatg != "17")
            {
                requestList.AddRange(_requestXml.GetRequestListByName("financial_contribution_js"));
                requestList.AddRange(_requestXml.GetRequestListByName("stock_change_js"));
                requestList.AddRange(_requestXml.GetRequestListByName("licence_js"));
                requestList.AddRange(_requestXml.GetRequestListByName("knowledge_property_js"));
                requestList.AddRange(_requestXml.GetRequestListByName("administrative_punishment_js"));
            }
            requestList.AddRange(_requestXml.GetRequestListByName("changerecord"));
            requestList.AddRange(_requestXml.GetRequestListByName("mortgage"));
            requestList.AddRange(_requestXml.GetRequestListByName("checkup"));
            if (!(requestInfo.Parameters.ContainsKey("platform") && "LIST_API" == requestInfo.Parameters["platform"]))
            {
                responseList.AddRange(_request.GetResponseInfo(requestList));
            }
            else
            {
                if (this._requestInfo.Parameters.ContainsKey("platform"))
                {
                    this._requestInfo.Parameters.Remove("platform");
                }
                _enterpriseInfo.parameters = this._requestInfo.Parameters;
            }
            
            this.ParseResponseMainInfo(responseList);

            SummaryEntity summaryEntity = new SummaryEntity();
            summaryEntity.Enterprise = _enterpriseInfo;
            summaryEntity.Abnormals = _abnormals;
            summaryEntity.Checkups = _checkups;
            return summaryEntity;


        }

        #region CreateRequest
        /// <summary>
        /// CreateRequest
        /// </summary>
        /// <returns></returns>
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

        void LoadAndParseResponseInfo(ResponseInfo responseInfo)
        {
            if (string.IsNullOrWhiteSpace(responseInfo.Data))
            {
                return;
            }
            if (responseInfo.Data != null && !responseInfo.Data.Contains("系统警告，累了！") && !responseInfo.Data.Contains("<iframe scrolling=\"no\" src=\"http://val2.bangruitech.com/asValidate"))
            {
                switch (responseInfo.Name)
                {
                    case "partner":
                        this.LoadAndParsePartner(responseInfo.Data);
                        break;
                    case "changerecord":
                        this.LoadAndParseChangeRecord(responseInfo.Data);
                        break;
                    case "employee":
                    case "employee_nz":
                        this.LoadAndParseEmployee(responseInfo.Data);
                        break;
                    case "branch":
                        this.LoadAndParseBranch(responseInfo.Data);
                        break;
                    case "mortgage":
                        this.LoadAndParseMortgagee(responseInfo.Data);
                        break;
                    case "equity_quality":
                        this.LoadAndParseEquityQuality(responseInfo.Data);
                        break;
                    case "judicial_freeze":
                      //  this.LoadAndParseJudicialFreeze(responseInfo.Data);
                        break;
                    case "abnormal_gt":
                        this.LoadAndParseAbnormal_GT(responseInfo.Data);
                        break;
                    case "abnormal":
                        this.LoadAndParseAbnormal(responseInfo.Data);
                        break;
                    case "checkup":
                        this.LoadAndParseCheckUp(responseInfo.Data);
                        break;
                    case "financial_contribution_js":
                        this.LoadAndParseFinancialContribution(responseInfo.Data);
                        break;
                    case "stock_change_js":
                        this.LoadAdnParseStockChange(responseInfo.Data);
                        break;
                    case "licence_js":
                        this.LoadAndParseLicence(responseInfo.Data);
                        break;
                    case "report":
                    case "report_gt":
                    case "report_nz":
                        this.LoadAndParseReports(responseInfo.Data);
                        break;
                    default:
                        break;
                }
            }
        }

        private void InitialEnterpriseInfo()
        {
            _enterpriseInfo.province = this._requestInfo.Province;
            _enterpriseInfo.last_update_time = DateTime.Now;
            _enterpriseInfo.source = "Batch";
            
        }

        private string ConvertDate(string dateStr)
        {
            if (String.IsNullOrEmpty(dateStr)) return dateStr;
            string outString = "";
            try
            {
                outString = DateTime.ParseExact(dateStr, "ddd MMM dd HH:mm:ss CST yyyy", new System.Globalization.CultureInfo("en-us")).ToString("yyyy-MM-dd");
            }
            catch
            {
                outString = dateStr;
            }

            return outString;
        }

        /// <summary>
        /// 解析工商公示信息
        /// </summary>
        /// <param name="responseInfoList"></param>
        private void ParseResponseMainInfo(List<ResponseInfo> responseInfoList)
        {
            try
            {
                Parallel.ForEach(responseInfoList, new ParallelOptions { MaxDegreeOfParallelism = 1 }, responseInfo => LoadAndParseResponseInfo(responseInfo));
            }
            catch (AggregateException ex){ }
        }

        #region 解析登记信息：基本信息
        /// <summary>
        /// 解析登记信息：基本信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseDengJiJiben(string responseData)
        {
            if (string.IsNullOrWhiteSpace(responseData))
            {
                Console.WriteLine("basicInfo is empty");
            }
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            var priPID = rootNode.SelectSingleNode("//input[@id='priPID']");
            if (priPID == null) return;
            var entTypeCatg = rootNode.SelectSingleNode("//input[@id='entTypeCatg']");
            if (entTypeCatg == null) return;
            var regNo = rootNode.SelectSingleNode("//input[@id='regNO']");
            if (regNo == null) return;
            var uniCode = rootNode.SelectSingleNode("//input[@id='uniCode']");
            if (uniCode == null) return;

            _requestInfo.Parameters.Add("priPID", priPID.Attributes["value"].Value);
            _requestInfo.Parameters.Add("regNO", regNo.Attributes["value"].Value);
            _requestInfo.Parameters.Add("uniCode", uniCode.Attributes["value"].Value);
            _entTypeCatg = entTypeCatg.Attributes["value"].Value;

            HtmlNodeCollection lis = rootNode.SelectNodes("//ul[@class='encounter-info clearfix']/li");
            if (lis != null)
            {
                foreach (HtmlNode li in lis)
                {
                    var spans = li.SelectNodes("./span");
                    var firstSpan = spans.First();
                    var em = firstSpan.SelectSingleNode("./em");
                    var title = firstSpan.InnerText.Replace(em.InnerText, "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").TrimEnd(new char[] { '：' });
                    var value = spans.Last().InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace("&nbsp;","").Trim();
                    switch (title)
                    {
                        case "注册号":
                            _enterpriseInfo.reg_no = value;
                            break;
                        case "统一社会信用代码":
                            _enterpriseInfo.credit_no = value;
                            break;
                        case "注册号/统一社会信用代码":
                        case "统一社会信用代码/注册号":
                            if (value.Length == 18)
                                _enterpriseInfo.credit_no = value;
                            else
                                _enterpriseInfo.reg_no = value;
                            break;
                        case "名称":
                        case "企业名称":
                            _enterpriseInfo.name = value;
                            break;
                        case "类型":
                            _enterpriseInfo.econ_kind = value;
                            break;
                        case "法定代表人":
                        case "负责人":
                        case "股东":
                        case "经营者":
                        case "执行事务合伙人":
                        case "投资人":
                            _enterpriseInfo.oper_name = value;
                            break;
                        case "住所":
                        case "经营场所":
                        case "营业场所":
                        case "主要经营场所":
                            Address address = new Address();
                            address.name = "注册地址";
                            address.address = value;
                            address.postcode = "";
                            _enterpriseInfo.addresses.Add(address);
                            break;
                        case "注册资金":
                        case "注册资本":
                        case "成员出资总额":
                            _enterpriseInfo.regist_capi = value;
                            break;
                        case "成立日期":
                        case "登记日期":
                        case "注册日期":
                            _enterpriseInfo.start_date = value;
                            break;
                        case "营业期限自":
                        case "经营期限自":
                        case "合伙期限自":
                            _enterpriseInfo.term_start = value;
                            break;
                        case "营业期限至":
                        case "经营期限至":
                        case "合伙期限至":
                            _enterpriseInfo.term_end = value;
                            break;
                        case "经营范围":
                        case "业务范围":
                            _enterpriseInfo.scope = value;
                            break;
                        case "登记机关":
                            _enterpriseInfo.belong_org = value;
                            break;
                        case "核准日期":
                            _enterpriseInfo.check_date = value;
                            break;
                        case "登记状态":
                            _enterpriseInfo.status = value;
                            break;
                        case "吊销日期":
                        case "吊销时间":
                        case "注销日期":
                        case "注销时间":
                            _enterpriseInfo.end_date = value;
                            break;
                        case "组成形式":
                            _enterpriseInfo.type_desc = value;
                            break;
                        default:
                            break;
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(_enterpriseInfo.reg_no) && string.IsNullOrWhiteSpace(_enterpriseInfo.credit_no) && string.IsNullOrWhiteSpace(_enterpriseInfo.name))
            {
                Console.WriteLine("---------------------------------------BASICINFO NO DATA START------------------------------------------");
                Console.WriteLine(responseData);
                Console.WriteLine("---------------------------------------BASICINFO NO DATA END------------------------------------------");
                LogHelper.Info(responseData);
            }
        }
        #endregion

        #region 解析登记信息：股东信息
        /// <summary>
        /// 解析登记信息：股东信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParsePartner(string responseData)
        {
            var request = this.CreateRequest();
            BsonDocument document = BsonDocument.Parse(responseData);
            if (document != null && document.Contains("recordsTotal") && document["recordsTotal"].AsInt32 > 0)
            {
                var arr = document.Contains("data") ? document["data"].AsBsonArray : new BsonArray();
                if (arr != null && arr.Any())
                {
                    foreach (BsonDocument item in arr)
                    {
                        Partner partner = new Partner();
                        partner.seq_no = _enterpriseInfo.partners.Count + 1;
                        partner.stock_name = item["inv"].IsBsonNull ? string.Empty : item["inv"].AsString;
                        if(item.Contains("invType"))
                        {
                            partner.stock_type = item["invType"].AsString == "1" ? "企业法人" : "自然人";
                            partner.identify_type = item["invType"].AsString == "1" ? "法人营业执照" : ((item.Contains("cerTypeName") && !item["cerTypeName"].IsBsonNull) ? item["cerTypeName"].AsString : string.Empty);
                            partner.identify_no = item["invType"].AsString == "1" ? ((item.Contains("bLicNO") && !item["bLicNO"].IsBsonNull) ? item["bLicNO"].AsString : string.Empty) : "( 非公示项 )";
                        }
                        if (item.Contains("id") && !item["id"].IsBsonNull)
                        {
                            var responseInfo = request.RequestData(new RequestSetting
                            {
                                Method = "get",
                                Url = string.Format("http://zj.gsxt.gov.cn/midinv/findMidInvById?midInvId={0}", item["id"].AsInt32),
                                Data = "",
                                IsArray = "0",
                                Name = "partnerdetail"
                            });
                            if (responseInfo != null)
                            {
                                this.LoadAndParsePartnerDetail(responseInfo.Data, partner);
                            }
                        }
                        _enterpriseInfo.partners.Add(partner);
                    }
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
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            var divs = rootNode.SelectNodes("//div[@class='mod-bd-panel_company']");
            if (divs != null && divs.Any())
            {
                foreach (var div in divs)
                {
                    var title = div.SelectSingleNode("./h3").InnerText;
                    var table_content = div.SelectSingleNode("./table");
                    if (title.Contains("认缴明细信息"))
                    {
                        var trs = table_content.SelectNodes("./tbody/tr");
                        if (trs != null && trs.Any())
                        {
                            foreach (var tr in trs)
                            {
                                var tds = tr.SelectNodes("./td");
                                if (tds != null && tds.Any())
                                {
                                    ShouldCapiItem sci = new ShouldCapiItem();
                                    sci.invest_type = tds[0].InnerText;
                                    sci.shoud_capi = tds[1].InnerText;
                                    sci.should_capi_date = tds[2].InnerText;
                                    partner.should_capi_items.Add(sci);
                                }
                            }

                        }
                    }
                    else if (title.Contains("实缴明细信息"))
                    {
                        var trs = table_content.SelectNodes("./tbody/tr");
                        if (trs != null && trs.Any())
                        {
                            foreach (var tr in trs)
                            {
                                var tds = tr.SelectNodes("./td");
                                if (tds != null && tds.Any())
                                {
                                    RealCapiItem rci = new RealCapiItem();
                                    rci.invest_type = tds[0].InnerText;
                                    rci.real_capi = tds[1].InnerText;
                                    rci.real_capi = tds[2].InnerText;
                                    partner.real_capi_items.Add(rci);
                                }
                            }
                        }
                    }
                }
                var divTotal = divs.First();
                var trsTotal = divTotal.SelectNodes("./table/tbody/tr");
                if (trsTotal != null && trsTotal.Any())
                {
                    foreach (var tr in trsTotal)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Any())
                        {
                            if (tds.First().InnerText.Contains("认缴额"))
                            {
                                partner.total_should_capi = tds[1].InnerText;
                            }
                            else if (tds.First().InnerText.Contains("实缴额"))
                            {
                                partner.total_real_capi = tds[1].InnerText;
                            }
                        }
                    }
                }

            }
        }
        #endregion

        #region 解析登记信息：变更信息
        /// <summary>
        /// 解析登记信息：变更信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseChangeRecord(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            if (document != null && document.Contains("recordsTotal") && document["recordsTotal"].AsInt32 > 0)
            {
                var arr = document.Contains("data") ? document["data"].AsBsonArray : new BsonArray();
                if (arr != null && arr.Any())
                {
                    foreach (BsonDocument item in arr)
                    {
                        if(string.IsNullOrEmpty(item["altContent"].IsBsonNull ? string.Empty : item["altContent"].AsString)
                            &&string.IsNullOrEmpty(item["altBeContent"].IsBsonNull ? string.Empty : item["altBeContent"].AsString)
                            && string.IsNullOrEmpty(item["altAfContent"].IsBsonNull ? string.Empty : item["altAfContent"].AsString)
                            &&string.IsNullOrEmpty(item["altDate"].IsBsonNull ? string.Empty : item["altDate"].AsString))
                        {
                            continue;
                        }
                        ChangeRecord changeRecord = new ChangeRecord();
                        changeRecord.seq_no = _enterpriseInfo.changerecords.Count + 1;
                        changeRecord.change_item = item["altContent"].IsBsonNull ? string.Empty : item["altContent"].AsString;
                        changeRecord.before_content = item["altBeContent"].IsBsonNull ? string.Empty : item["altBeContent"].AsString;
                        changeRecord.after_content = item["altAfContent"].IsBsonNull ? string.Empty : item["altAfContent"].AsString;
                        changeRecord.change_date = item["altDate"].IsBsonNull ? string.Empty : item["altDate"].AsString;
                        
                        _enterpriseInfo.changerecords.Add(changeRecord);
                    }
                }
            }
        }
        #endregion

        #region 解析主要人员
        /// <summary>
        /// 解析主要人员
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseEmployee(string responseData)
        {
            object[] anonymous = { new { name = string.Empty, posiContent = String.Empty, sex = string.Empty, cerNO = string.Empty } };
            var arr = JsonConvert.DeserializeAnonymousType(responseData, anonymous);
            if (arr != null && arr.Any())
            {
                foreach (object obj in arr)
                {
                    BsonDocument item = BsonDocument.Parse(obj.ToString());
                    Employee employee = new Employee();
                    employee.seq_no = _enterpriseInfo.employees.Count + 1;
                    if (_entTypeCatg == "11" || _entTypeCatg == "31" || _entTypeCatg == "21")
                    {
                        employee.name = item["name"].IsBsonNull ? string.Empty : item["name"].AsString;
                        employee.job_title = item["posiContent"].IsBsonNull ? string.Empty : item["posiContent"].AsString;
                        employee.sex = item["sex"].IsBsonNull ? string.Empty : item["sex"].AsString;
                        employee.cer_no = item["cerNO"].IsBsonNull ? string.Empty : item["cerNO"].AsString;
                        _enterpriseInfo.employees.Add(employee);
                    }
                    if (_entTypeCatg == "16")
                    {
                        //农专成员名册

                    }
                }
            }
        
        }
        #endregion

        #region 解析分支机构
        /// <summary>
        /// 解析分支机构
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseBranch(string responseData)
        {
            object[] anonymous = { new { entName = string.Empty, regNO = String.Empty, regOrgName = string.Empty } };
            var arr = JsonConvert.DeserializeAnonymousType(responseData, anonymous);
            if (arr != null && arr.Any())
            {
                foreach (object obj in arr)
                {
                    BsonDocument item = BsonDocument.Parse(obj.ToString());
                    Branch branch = new Branch();
                    branch.seq_no = _enterpriseInfo.branches.Count + 1;

                    branch.name = item["entName"].IsBsonNull ? string.Empty : item["entName"].AsString;
                    branch.reg_no = item["regNO"].IsBsonNull ? string.Empty : item["regNO"].AsString;
                    branch.belong_org = item["regOrgName"].IsBsonNull ? string.Empty : item["regOrgName"].AsString;
                    _enterpriseInfo.branches.Add(branch);
                }
            }
        }
        #endregion

        #region 解析动产抵押信息
        /// <summary>
        /// 解析动产抵押信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseMortgagee(string responseData)
        {
            var request = this.CreateRequest();
            BsonDocument document = BsonDocument.Parse(responseData);
            if (document != null && document.Contains("recordsTotal") && document["recordsTotal"].AsInt32 > 0)
            {
                var arr = document.Contains("data") ? document["data"].AsBsonArray : new BsonArray();
                if (arr != null && arr.Any())
                {
                    foreach (BsonDocument item in arr)
                    {
                        MortgageInfo mortgagee = new MortgageInfo();
                        mortgagee.seq_no = _enterpriseInfo.mortgages.Count + 1;
                        mortgagee.number = item["filingNO"].IsBsonNull ? string.Empty : item["filingNO"].AsString;
                        mortgagee.department = item["departMentName"].IsBsonNull ? string.Empty : item["departMentName"].AsString;
                        mortgagee.date = item["checkDate"].IsBsonNull ? string.Empty : item["checkDate"].AsString;
                        mortgagee.amount = item["mortGageAmount"].IsBsonNull ? string.Empty : item["mortGageAmount"].AsDouble.ToString();
                        mortgagee.debit_amount = item["mortGageAmount"].IsBsonNull ? string.Empty : item["mortGageAmount"].AsDouble.ToString();
                        mortgagee.status = item["cancelStatus"].IsBsonNull ? string.Empty : (item["cancelStatus"].AsString == "0" ? "有效" : "-");
                        mortgagee.public_date = item["checkDate"].IsBsonNull ? string.Empty : item["checkDate"].AsString;
                        if (item.Contains("id") && !item["id"].IsBsonNull)
                        {
                            var responseInfo = request.RequestData(new RequestSetting
                            {
                                Method = "get",
                                Url = string.Format("http://zj.gsxt.gov.cn/pub/mortreginfo/detail?id={0}", item["id"].AsInt32),
                                Data = "",
                                IsArray = "0",
                                Name = "mortgagedetail"
                            });
                            if (responseInfo != null)
                            {
                                this.LoadAndParseMortgageDetail(responseInfo.Data, mortgagee);
                            }
                        }
                        _enterpriseInfo.mortgages.Add(mortgagee);
                    }
                }
            }
        }

        #endregion

        #region 解析动产抵押详情
        /// <summary>
        /// 解析动产抵押详情
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="mortgage"></param>
        void LoadAndParseMortgageDetail(string responseData, MortgageInfo mortgage)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            var divs = rootNode.SelectNodes("//div[@class='mod-bd-panel_company']");
            if (divs != null && divs.Any())
            {
                foreach (var div in divs)
                {
                    var title = div.SelectSingleNode("./h3").InnerText;
                    var table_content = div.SelectSingleNode("./table");
                    if (title.Contains("抵押权人概况信息"))
                    {
                        var trs = table_content.SelectNodes("./tbody/tr");
                        if (trs != null && trs.Any())
                        {
                            foreach (var tr in trs)
                            {
                                var tds = tr.SelectNodes("./td");
                                if (tds != null && tds.Any())
                                {
                                    Mortgagee mortgagee = new Mortgagee();
                                    mortgagee.seq_no = mortgage.mortgagees.Count + 1;
                                    mortgagee.name = tds[1].InnerText;
                                    mortgagee.identify_type = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    mortgagee.identify_no = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    mortgage.mortgagees.Add(mortgagee);
                                }
                            }

                        }
                    }
                    else if (title.Contains("被担保债权概况信息"))
                    {
                        var trs = table_content.SelectNodes("./tbody/tr");
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
                                                mortgage.debit_type = tds[i + 1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                                break;
                                            case "数额":
                                                mortgage.debit_amount = tds[i + 1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                                break;
                                            case "担保的范围":
                                                mortgage.debit_scope = tds[i + 1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                                break;
                                            case "债务人履行债务的期限":
                                                mortgage.debit_period = tds[i + 1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                                break;
                                            case "备注":
                                                mortgage.debit_remarks = tds[i + 1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                                break;
                                            default:
                                                break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else if (title.Contains("抵押物概况信息"))
                    {
                        var trs = table_content.SelectNodes("./tbody/tr");
                        if (trs != null && trs.Any())
                        {
                            foreach (var tr in trs)
                            {
                                var tds = tr.SelectNodes("./td");
                                if (tds != null && tds.Any())
                                {
                                    Guarantee guarantee = new Guarantee();
                                    guarantee.seq_no = mortgage.guarantees.Count + 1;
                                    guarantee.name = tds[1].InnerText;
                                    guarantee.belong_to = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    guarantee.desc = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    guarantee.remarks = tds.Last().InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    mortgage.guarantees.Add(guarantee);
                                }
                            }

                        }
                    }
                }
            }
        }
        #endregion

        #region 股权出质登记信息
        /// <summary>
        ///  股权出质登记信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseEquityQuality(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            if (document != null && document.Contains("recordsTotal") && document["recordsTotal"].AsInt32 > 0)
            {
                var arr = document.Contains("data") ? document["data"].AsBsonArray : new BsonArray();
                if (arr != null && arr.Any())
                {
                    foreach (BsonDocument item in arr)
                    {
                        EquityQuality equityQuality = new EquityQuality();
                        equityQuality.seq_no = _enterpriseInfo.equity_qualities.Count + 1;
                        equityQuality.number = item["orderNO"].IsBsonNull ? string.Empty : item["orderNO"].AsString;
                        equityQuality.pledgor = item["pledgor"].IsBsonNull ? string.Empty : item["pledgor"].AsString;
                        equityQuality.pledgor_identify_no = equityQuality.pledgor.Length < 6 ? "( 非公示项 )" : item["pleBLicNO"].AsString;
                        equityQuality.pledgor_amount = item["impAm"].IsBsonNull ? string.Empty : item["impAm"].AsDouble.ToString();
                        equityQuality.pledgor_amount = string.IsNullOrWhiteSpace(equityQuality.pledgor_amount) ? "" : equityQuality.pledgor_amount + "万元";
                        equityQuality.pawnee = item["impOrg"].IsBsonNull ? string.Empty : item["impOrg"].AsString;
                        equityQuality.pawnee_identify_no = equityQuality.pawnee.Length < 6 ? "( 非公示项 )" : item["impBLicNO"].AsString;
                        equityQuality.date = item["equPleDate"].IsBsonNull ? string.Empty : item["equPleDate"].AsString;
                        equityQuality.status = item["status"].IsBsonNull ? string.Empty : item["status"].AsString;
                        equityQuality.status = (equityQuality.status == "K" || equityQuality.status == "B") ? "有效" : "无效";
                        equityQuality.public_date = item["recDate"].IsBsonNull ? string.Empty : item["recDate"].AsString;
                        _enterpriseInfo.equity_qualities.Add(equityQuality);
                    }
                }
            }
        }
        #endregion

        #region 解析股权冻结信息
        private class ZJJudicialDetailInfo
        {
            public string id { get; set; }
            public string priPID { get; set; }
            public string justiceType { get; set; }
            public string deptCode { get; set; }
            public string executionCourt { get; set; }
            public string exeRulNum { get; set; }
            public string botRefNum { get; set; }
            public string executeNo { get; set; }
            public string executeItem { get; set; }
            public string inv { get; set; }
            public string cerType { get; set; }
            public string cerNO { get; set; }
            public string entName { get; set; }
            public string uniSCID { get; set; }
            public string froAm { get; set; }
            public string froAuth { get; set; }
            public string frozDeadline { get; set; }
            public string froFrom { get; set; }
            public string froTo { get; set; }
            public string thawDate { get; set; }
            public string loseEffDate { get; set; }
            public string loseEffRes { get; set; }
            public string publicDate { get; set; }
            public string frozState { get; set; }
            public string assInv { get; set; }
            public string assCerType { get; set; }
            public string assCerNO { get; set; }
            public string executeDate { get; set; }
            public string setName { get; set; }
            public string setDate { get; set; }
            public string auditName { get; set; }
            public string auditDate { get; set; }
            public string auditOpin { get; set; }
            public string auditState { get; set; }
            public string justiceConNO { get; set; }
            public string createTime { get; set; }
            public string frozStateName { get; set; }
            public string uid { get; set; }
        }
        private class ZJJudicialInfo
        {
            public int draw { get; set; }
            public int recordsTotal { get; set; }
            public int recordsFiltered { get; set; }
            public ZJJudicialDetailInfo[] data { get; set; }
            public object error { get; set; }
        }

        /// <summary>
        /// 解析股权冻结信息
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseJudicialFreeze(string responseData)
        {
            var zjJudicialInfo = JsonConvert.DeserializeObject<ZJJudicialInfo>(responseData);
            JudicialFreeze freeze = new JudicialFreeze();
            foreach(var info in zjJudicialInfo.data)
            {
                freeze.be_executed_person = info.inv;
                freeze.amount = info.froAm+info.froAuth;
                freeze.executive_court = info.executionCourt;
                freeze.number = info.executeNo;
                freeze.status = info.justiceType == "1" ? "股权冻结|" + (info.frozStateName == "null" ? string.Empty : info.frozStateName) : "股权变更";
                freeze.type = info.justiceType == "1" ? "股权冻结" : "股权变更";
                if (info.justiceType == "1")
                {
                    freeze.detail.execute_court = info.executionCourt;
                    freeze.detail.assist_item = switchItemCode(info.justiceType);
                    freeze.detail.adjudicate_no = info.executeNo;
                    freeze.detail.notice_no = info.botRefNum;
                    freeze.detail.assist_name = info.inv;
                    freeze.detail.freeze_amount = info.froAm + info.froAuth;
                    freeze.detail.assist_ident_type = SwitchCerTypeCode(info.cerType);
                    freeze.detail.assist_ident_no = info.cerNO;
                    freeze.detail.freeze_start_date = info.froFrom;
                    freeze.detail.freeze_end_date = info.froTo;
                    freeze.detail.freeze_year_month = info.frozDeadline;
                    freeze.detail.public_date = info.publicDate;
                }
                else
                {
                    freeze.pc_freeze_detail.execute_court = info.executionCourt;
                    freeze.pc_freeze_detail.assist_item = switchItemCode(info.justiceType);
                    freeze.pc_freeze_detail.adjudicate_no = info.executeNo;
                    freeze.pc_freeze_detail.notice_no = info.botRefNum;
                    freeze.pc_freeze_detail.assist_name = info.inv;
                    freeze.pc_freeze_detail.freeze_amount = info.froAm + info.froAuth;
                    freeze.pc_freeze_detail.assist_ident_type = SwitchCerTypeCode(info.cerType);
                    freeze.pc_freeze_detail.assist_ident_no = info.cerNO;
                    freeze.pc_freeze_detail.assignee = info.assInv;
                    freeze.pc_freeze_detail.xz_execute_date = info.executeDate;
                    freeze.pc_freeze_detail.assignee_ident_type = SwitchCerTypeCode(info.assCerType);
                    freeze.pc_freeze_detail.assist_ident_no = info.assCerNO;
                }

            }
        }


        private string SwitchCerTypeCode(string val)
        {
            if (val == "10")
            {
                return "居民身份证";
            }
            else if (val == "20")
            {
                return "军官证";
            }
            else if (val == "30")
            {
                return "警官证";
            }
            else if (val == "40")
            {
                return "外国地区护照";
            }
            else if (val == "52")
            {
                return "香港身份证";
            }
            else if (val == "54")
            {
                return "澳门身份证";
            }
            else if (val == "56")
            {
                return "台湾身份证";
            }
            else
            {
                return "其他有效身份证件";
            }
        }
    

/* 执行事项 */
        private string switchItemCode(string val)
        {
            if (val == "1")
            {
                return "公示冻结股权、其他投资权益";
            }
            else if (val == "2")
            {
                return "续行冻结股权、其他投资权益";
            }
            else if (val == "3")
            {
                return "解除冻结股权、其他投资权益";
            }
            else if (val == "4")
            {
                return "强制转让被执行人股权，办理有限责任公司股东变更登记";
            }
            return string.Empty;
        }

/* 失效原因 */
        private string switchLoseEffResCode(string val)
        {
            if (val == "1")
            {
                return "冻结期满且未续行冻结，自动失效";
            }
            else if (val == "2")
            {
                return "2014年11月30日前未设定期限的冻结，公示满2年，未续行冻结，自动失效";
            }
            return string.Empty;

        }
        #endregion

        #region 经营异常
        /// <summary>
        /// 经营异常
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseAbnormal(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            if (document != null && document.Contains("recordsTotal") && document["recordsTotal"].AsInt32 > 0)
            {
                var arr = document.Contains("data") ? document["data"].AsBsonArray : new BsonArray();
                if (arr != null && arr.Any())
                {
                    foreach (BsonDocument item in arr)
                    {
                        AbnormalInfo abnormalInfo = new AbnormalInfo();
                        abnormalInfo.province = _enterpriseInfo.province;
                        abnormalInfo.reg_no = _enterpriseInfo.reg_no;
                        abnormalInfo.name = _enterpriseInfo.name;
                        abnormalInfo.in_reason = item["speCauseCN"].IsBsonNull ? string.Empty : item["speCauseCN"].AsString;
                        abnormalInfo.in_date = item["abnTime"].IsBsonNull ? string.Empty : item["abnTime"].AsString;
                        abnormalInfo.department = item["decorgCN"].IsBsonNull ? string.Empty : item["decorgCN"].AsString;
                        abnormalInfo.out_reason = item["remExcpresCN"].IsBsonNull ? string.Empty : item["remExcpresCN"].AsString;
                        abnormalInfo.out_date = item["remDate"].IsBsonNull ? string.Empty : item["remDate"].AsString;
                        _abnormals.Add(abnormalInfo);
                    }
                }
            }
        }
        #endregion

        #region 经营异常--个体
        /// <summary>
        /// 经营异常
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseAbnormal_GT(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            if (document != null && document.Contains("recordsTotal") && document["recordsTotal"].AsInt32 > 0)
            {
                var arr = document.Contains("data") ? document["data"].AsBsonArray : new BsonArray();
                if (arr != null && arr.Any())
                {
                    foreach (BsonDocument item in arr)
                    {
                        AbnormalInfo abnormalInfo = new AbnormalInfo();
                        abnormalInfo.province = _enterpriseInfo.province;
                        abnormalInfo.reg_no = _enterpriseInfo.reg_no;
                        abnormalInfo.name = _enterpriseInfo.name;
                        abnormalInfo.in_reason = item["excpStaResCN"].IsBsonNull ? string.Empty : item["excpStaResCN"].AsString;
                        abnormalInfo.in_date = item["cogDate"].IsBsonNull ? string.Empty : item["cogDate"].AsString;
                        abnormalInfo.department = item["decorgCN"].IsBsonNull ? string.Empty : item["decorgCN"].AsString;
                        abnormalInfo.out_reason = item["norReaCN"].IsBsonNull ? string.Empty : item["norReaCN"].AsString;
                        abnormalInfo.out_date = item["norDate"].IsBsonNull ? string.Empty : item["norDate"].AsString;
                        _abnormals.Add(abnormalInfo);
                    }
                }
            }
        }
        #endregion

        #region 解析抽查检查信息
        /// <summary>
        /// 解析抽查检查信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseCheckUp(string responseData)
        {
            
            BsonDocument document = BsonDocument.Parse(responseData);
            if (document != null && document.Contains("recordsTotal") && document["recordsTotal"].AsInt32 > 0)
            {
                var arr = document.Contains("data") ? document["data"].AsBsonArray : new BsonArray();
                if (arr != null && arr.Any())
                {
                    foreach (BsonDocument item in arr)
                    {

                        CheckupInfo checkupInfo = new CheckupInfo();
                        checkupInfo.province = _enterpriseInfo.province;
                        checkupInfo.reg_no = _enterpriseInfo.reg_no;
                        checkupInfo.name = _enterpriseInfo.name;
                        checkupInfo.department = item["inspectDesc"].IsBsonNull ? string.Empty : item["inspectDesc"].AsString;
                        checkupInfo.type = item["scType"].IsBsonNull ? string.Empty : item["scType"].AsString;
                        string date = item["inspectDate"].IsBsonNull ? string.Empty : item["inspectDate"].AsString; ;
                        if(date.Contains(" "))
                        {
                            checkupInfo.date = date.Split(' ')[0];
                        }
                        else
                        {
                            checkupInfo.date = string.Empty;
                        }
                        
                        checkupInfo.result = item["scResult"].IsBsonNull ? string.Empty : item["scResult"].AsString;

                        if (checkupInfo.result == "1")
                        {
                            checkupInfo.result = "正常;";
                        }
                        else if (checkupInfo.result == "2")
                        {
                            checkupInfo.result = "未按规定公示即时信息;";
                        }
                        else if (checkupInfo.result == "3")
                        {
                            checkupInfo.result = "未按规定公示年报信息;";
                        }
                        else if (checkupInfo.result == "4")
                        {
                            checkupInfo.result = "通过登记的住所（经营场所）无法联系;";
                        }
                        else if (checkupInfo.result == "5")
                        {
                            checkupInfo.result = "公示信息隐瞒真实情况、弄虚作假;";
                        }
                        else if (checkupInfo.result == "6")
                        {
                            checkupInfo.result = "不予配合情节严重;";
                        }
                        else if (checkupInfo.result == "7")
                        {
                            checkupInfo.result = "已注销;";
                        }
                        else if (checkupInfo.result == "8")
                        {
                            checkupInfo.result = "被吊销营业执照;";
                        }
                        else if (checkupInfo.result == "9")
                        {
                            checkupInfo.result = "被撤销;";
                        }
                        else
                        {
                            checkupInfo.result = "正常;";
                        }
                        _checkups.Add(checkupInfo);
                    }
                }
            }
        }
        #endregion

        #region 解析企业年报
        /// <summary>
        /// 解析企业年报
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseReports(string responseData)
        {
            var request = this.CreateRequest();
            BsonDocument document = BsonDocument.Parse(responseData);
            if (document != null && document.Contains("recordsTotal") && document["recordsTotal"].AsInt32 > 0)
            {
                var arr = document.Contains("data") ? document["data"].AsBsonArray : new BsonArray();
                if (arr != null && arr.Any())
                {
                    foreach (BsonDocument item in arr)
                    {
                        Report report = new Report();
                        var reportType = item.Contains("reportType") ? (item["reportType"].IsBsonNull ? string.Empty : item["reportType"].AsString) : string.Empty;
                        List<ResponseInfo> responseList = new List<ResponseInfo>();
                        if (_entTypeCatg == "16" || _entTypeCatg == "17")
                        {
                            //initDataTable_sfc_licenceinfo(_year);//农专年报行政许可
                            //initDataTable_sfc_websiteinfo(_year);//农专网站或网店信息
                            //initDataTable_sfc_branchinfo(_year);//农专分支机构信息
                            //if(window._CONFIG.pageType!='print') initDataTable_sfc_updateinfo();//农专年报修改
                        }
                        else if (_entTypeCatg == "50")
                        {
                            //initDataTable_pb_licenceinfo(_year);//个体户年报行政许可
                            //initDataTable_pb_websiteinfo(_year);//个体户网站或网店信息
                            //if(window._CONFIG.pageType!='print') initDataTable_pb_updateinfo();//个体户年报修改
                            var year = item["year"].IsBsonNull ? string.Empty : item["year"].AsInt32.ToString();
                            report.report_name = string.Format("{0}年度报告", year);
                            report.report_year = item["year"].IsBsonNull ? string.Empty : item["year"].AsInt32.ToString();
                            report.report_date = item["ancheDate"].IsBsonNull ? string.Empty : item["ancheDate"].AsString;

                            if (!string.IsNullOrWhiteSpace(reportType) && reportType != "6") 
                            {
                                request.AddOrUpdateRequestParameter("year", item["year"].IsBsonNull ? string.Empty : item["year"].AsInt32.ToString());
                                responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("report_gt"));
                                if (responseList != null && responseList.Any())
                                {
                                    foreach (var responseInfo in responseList)
                                    {
                                        switch (responseInfo.Name)
                                        {
                                            case "report_basic":
                                                this.LoadAndParseReportBasicInfo_GT(responseInfo.Data, report);
                                                break;
                                            case "report_website":
                                                this.LoadAndParseReportWebsite(responseInfo.Data, report);
                                                break;
                                            default:
                                                break;
                                        }
                                    }
                                }
                            }
                            
                        }
                        else
                        {
                            request.AddOrUpdateRequestParameter("anCheID", item["anCheID"].IsBsonNull ? string.Empty : item["anCheID"].AsString);
                            report.report_name = item["anCheName"].IsBsonNull ? string.Empty : item["anCheName"].AsString;
                            report.report_year = item["year"].IsBsonNull ? string.Empty : item["year"].AsInt32.ToString();
                            report.report_date = item["ancheDateStr"].IsBsonNull ? string.Empty : item["ancheDateStr"].AsString;
                            report.reg_no = item["regNO"].IsBsonNull ? string.Empty : item["regNO"].AsString;
                            report.credit_no = item["uniCode"].IsBsonNull ? string.Empty : item["uniCode"].AsString;
                            report.name = item["entName"].IsBsonNull ? string.Empty : item["entName"].AsString;
                            report.address = item["addr"].IsBsonNull ? string.Empty : item["addr"].AsString;
                            report.zip_code = item["postalCode"].IsBsonNull ? string.Empty : item["postalCode"].AsString;
                            report.telephone = item["tel"].IsBsonNull ? string.Empty : item["tel"].AsString;
                            report.email = item["email"].IsBsonNull ? string.Empty : item["email"].AsString;
                            report.collegues_num = item["empNumStr"].IsBsonNull ? string.Empty : item["empNumStr"].AsString;
                            report.status = item["busStatusCN"].IsBsonNull ? string.Empty : item["busStatusCN"].AsString;
                            report.if_website = item["ifWebSite"].IsBsonNull ? string.Empty : item["ifWebSite"].AsString;
                            report.if_website = report.if_website == "0" ? "否" : "是";
                            report.if_invest = item["ifForInvest"].IsBsonNull ? string.Empty : item["ifForInvest"].AsString;
                            report.if_invest = report.if_invest == "0" ? "否" : "是";
                            report.if_external_guarantee = item["ifForguarantee"].IsBsonNull ? string.Empty : item["ifForguarantee"].AsString;
                            report.if_external_guarantee = report.if_external_guarantee == "0" ? "否" : "是";
                            report.if_equity = item["ifAleErstock"].IsBsonNull ? string.Empty : item["ifAleErstock"].AsString;
                            report.if_equity = report.if_equity == "0" ? "否" : "是";

                            report.total_equity = item["assGroStr"].IsBsonNull ? string.Empty : item["assGroStr"].AsString;
                            report.sale_income = item["vendIncStr"].IsBsonNull ? string.Empty : item["vendIncStr"].AsString;
                            report.serv_fare_income = item["maiBusIncStr"].IsBsonNull ? string.Empty : item["maiBusIncStr"].AsString;
                            report.tax_total = item["ratGroStr"].IsBsonNull ? string.Empty : item["ratGroStr"].AsString;
                            report.profit_reta = item["totEquStr"].IsBsonNull ? string.Empty : item["totEquStr"].AsString;
                            report.profit_total = item["proGroStr"].IsBsonNull ? string.Empty : item["proGroStr"].AsString;
                            report.net_amount = item["netIncStr"].IsBsonNull ? string.Empty : item["netIncStr"].AsString;
                            report.debit_amount = item["liaGroStr"].IsBsonNull ? string.Empty : item["liaGroStr"].AsString;
                            if (item.Contains("disOpers") && !item["disOpers"].IsBsonNull && item["disOpers"].AsInt32 == 1)
                            {
                                report.social_security.yanglaobx_num = item["endowmentNum"].IsBsonNull ? string.Empty : item["endowmentNum"].AsInt32.ToString() + "人";
                                report.social_security.shiyebx_num = item["unemploymentNum"].IsBsonNull ? string.Empty : item["unemploymentNum"].AsInt32.ToString() + "人";
                                report.social_security.yiliaobx_num = item["medicalNum"].IsBsonNull ? string.Empty : item["medicalNum"].AsInt32.ToString() + "人";
                                report.social_security.gongshangbx_num = item["empInjuryNum"].IsBsonNull ? string.Empty : item["empInjuryNum"].AsInt32.ToString() + "人";
                                report.social_security.shengyubx_num = item["maternityNum"].IsBsonNull ? string.Empty : item["maternityNum"].AsInt32.ToString() + "人";
                                report.social_security.dw_yanglaobx_js = this.ConvertToStr(item, "actualPayEndowment");
                                report.social_security.dw_shiyebx_js = this.ConvertToStr(item, "actualPayUnemployment"); 
                                report.social_security.dw_yiliaobx_js = this.ConvertToStr(item, "actualPayMedical"); 
                                report.social_security.dw_shengyubx_js = this.ConvertToStr(item, "actualPayMaternity"); 
                                report.social_security.bq_yanglaobx_je = this.ConvertToStr(item, "paymentEndowment"); 
                                report.social_security.bq_shiyebx_je = this.ConvertToStr(item, "paymentUnemployment"); 
                                report.social_security.bq_yiliaobx_je = this.ConvertToStr(item, "paymentMedical"); 
                                report.social_security.bq_gongshangbx_je = this.ConvertToStr(item, "paymentEmpInjury"); 
                                report.social_security.bq_shengyubx_je = this.ConvertToStr(item, "paymentMaternity"); 
                                report.social_security.dw_yanglaobx_je = this.ConvertToStr(item, "cumuEndowment");
                                report.social_security.dw_shiyebx_je = this.ConvertToStr(item, "cumuUnemployment"); 
                                report.social_security.dw_yiliaobx_je = this.ConvertToStr(item, "cumuMedical"); 
                                report.social_security.dw_gongshangbx_je = this.ConvertToStr(item, "cumuEmpInjury"); 
                                report.social_security.dw_shengyubx_je = this.ConvertToStr(item, "cumuMaternity"); 
                            }

                            responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("report"));
                            if (responseList != null && responseList.Any())
                            {
                                foreach (var responseInfo in responseList)
                                {
                                    switch (responseInfo.Name)
                                    {
                                        case "report_website":
                                            this.LoadAndParseReportWebsite(responseInfo.Data,report);
                                            break;
                                        case "report_partner":
                                            this.LoadAndParseReportPartner(responseInfo.Data, report);
                                            break;
                                        case "report_invest":
                                            this.LoadAndParseReportInvest(responseInfo.Data, report);
                                            break;
                                        case "report_guarantee":
                                            this.LoadAndParseReportGuarantee(responseInfo.Data, report);
                                            break;
                                        case "report_stockchange":
                                            this.LoadAndParseReportStockChange(responseInfo.Data, report);
                                            break;
                                    }
                                }
                            }
                        }
                        _enterpriseInfo.reports.Add(report);
                    }
                }
            }
        }
        #endregion

        string ConvertToStr(BsonDocument item, string fieldName)
        {
            var result = "企业选择不公示";
            if (item.Contains(fieldName))
            {
                if (item[fieldName].BsonType==BsonType.String)
                {
                    result = item[fieldName].AsString;
                }
                else if (item[fieldName].BsonType == BsonType.Int32)
                {
                    result = item[fieldName].AsInt32.ToString();
                }
                else if (item[fieldName].BsonType == BsonType.Int64)
                {
                    result = item[fieldName].AsInt64.ToString();
                }
                else if (item[fieldName].BsonType == BsonType.Double)
                {
                    result = item[fieldName].AsDouble.ToString();
                }
            }
            else
            {
                result = string.Empty;
            }
            return result;
        }

        #region 解析年报基本信息--个体
        void LoadAndParseReportBasicInfo_GT(string responseData,Report report)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var lis = rootNode.SelectNodes("//ul[@class='encounter-info clearfix']/li");
            if (lis != null && lis.Any())
            {
                foreach (var li in lis)
                {
                    var em = li.SelectSingleNode("./em");
                    var spans = li.SelectNodes("./span");
                    var title = spans.First().InnerText;
                    var val = spans.Last().InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                    switch (title)
                    {
                        case "注册号：":
                            report.reg_no = val;
                            break;
                        case "个体户名称：":
                            report.name = val;
                            break;
                        case "经营者名称：":
                            report.oper_name = val;
                            break;
                        case "经营者联系电话：":
                            report.telephone = val;
                            break;
                        case "资金数额：":
                            report.reg_capi = val;
                            break;
                        case "从业人数：":
                            report.collegues_num = val;
                            break;
                        case "是否有网站或网店：":
                            report.if_website = val;
                            break;
                        default:
                            break;
                    }
                }
            }
            var table = rootNode.SelectSingleNode("//table[@class='table-common table-zichan']");
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                foreach (var tr in trs)
                {
                    var tds = tr.SelectNodes("./td");
                    if (tds != null && tds.Any())
                    {
                        for (int i = 0; i < tds.Count; i += 2)
                        {
                            switch (tds[i].InnerText)
                            { 
                                case "营业额或营业总收入":
                                    report.sale_income = tds[i + 1].InnerText;
                                    break;
                                case "纳税总额":
                                    report.tax_total = tds[i + 1].InnerText;
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

        #region 解析网站信息--年报
        /// <summary>
        /// 解析网站信息--年报
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportWebsite(string responseData, Report report)
        {
            object[] anonymous = { new { webSite = string.Empty, webType = String.Empty, webSitName = string.Empty } };
            var arr = JsonConvert.DeserializeAnonymousType(responseData, anonymous);
            if (arr != null && arr.Any())
            {
                foreach (object obj in arr)
                {
                    BsonDocument item = BsonDocument.Parse(obj.ToString());
                    WebsiteItem website = new WebsiteItem();
                    website.seq_no = report.websites.Count + 1;
                    website.web_name = item["webSitName"].IsBsonNull ? string.Empty : item["webSitName"].AsString;
                    website.web_url = item["webSite"].IsBsonNull ? string.Empty : item["webSite"].AsString;
                    website.web_type = item["webType"].IsBsonNull ? string.Empty : item["webType"].AsString;
                    website.web_type = website.web_type == "1" ? "网站" : "网店";
         
                    report.websites.Add(website);
                }
            }
        }
        #endregion

        #region 解析股东及出资信息--年报
        /// <summary>
        /// 年报股东
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportPartner(string responseData, Report report)
        {

            BsonDocument document = BsonDocument.Parse(responseData);
            if (document != null && document.Contains("recordsTotal") && document["recordsTotal"].AsInt32 > 0)
            {
                var arr = document.Contains("data") ? document["data"].AsBsonArray : new BsonArray();
                if (arr != null && arr.Any())
                {
                    foreach (BsonDocument item in arr)
                    {
                        Partner partner = new Partner();

                        partner.seq_no = report.partners.Count + 1;
                        partner.stock_name = item["inv"].IsBsonNull ? string.Empty : item["inv"].AsString;
                        ShouldCapiItem sci = new ShouldCapiItem();
                        sci.shoud_capi = item["lisubconam"].IsBsonNull ? string.Empty : item["lisubconam"].AsDouble.ToString();
                        sci.should_capi_date = item["subConDate"].IsBsonNull ? string.Empty : item["subConDate"].AsString;
                        sci.invest_type = item["conFormCN"].IsBsonNull ? string.Empty : item["conFormCN"].AsString;
                        partner.should_capi_items.Add(sci);

                        RealCapiItem rci = new RealCapiItem();
                        rci.real_capi = item["liacconam"].IsBsonNull ? string.Empty : item["liacconam"].AsDouble.ToString();
                        rci.real_capi_date = item["acConDate"].IsBsonNull ? string.Empty : item["acConDate"].AsString;
                        rci.invest_type = item["acConFormCn"].IsBsonNull ? string.Empty : item["acConFormCn"].AsString;
                        partner.real_capi_items.Add(rci);
                        report.partners.Add(partner);
                    }
                }
            }
        }
        #endregion

        #region 解析对外投资信息--年报
        /// <summary>
        /// 解析对外投资信息--年报
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        void LoadAndParseReportInvest(string responseData, Report report)
        {
            
            object[] anonymous = { new { webSite = string.Empty, webType = String.Empty, webSitName = string.Empty } };
            var arr = JsonConvert.DeserializeAnonymousType(responseData, anonymous);
            if (arr != null && arr.Any())
            {
                foreach (object obj in arr)
                {
                    BsonDocument item = BsonDocument.Parse(obj.ToString());
                    InvestItem investItem = new InvestItem();
                    investItem.seq_no = report.invest_items.Count + 1;
                    investItem.invest_name = item["entName"].IsBsonNull ? string.Empty : item["entName"].AsString;
                    investItem.invest_reg_no = item["uniCode"].IsBsonNull ? string.Empty : item["uniCode"].AsString;
                    report.invest_items.Add(investItem);
                }
            }
        }
        #endregion

        #region 解析股权变更--年报
        /// <summary>
        /// 解析股权变更--年报
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportStockChange(string responseData, Report report)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            if (document != null && document.Contains("recordsTotal") && document["recordsTotal"].AsInt32 > 0)
            {
                var arr = document.Contains("data") ? document["data"].AsBsonArray : new BsonArray();
                if (arr != null && arr.Any())
                {
                    foreach (BsonDocument item in arr)
                    {
                        StockChangeItem sci = new StockChangeItem();
                        sci.seq_no = report.stock_changes.Count + 1;
                        sci.name = item["inv"].IsBsonNull ? string.Empty : item["inv"].AsString;
                        sci.before_percent = item["beTransAmPr"].IsBsonNull ? string.Empty : item["beTransAmPr"].AsDouble.ToString() + "%";
                        sci.after_percent = item["afTransAmPr"].IsBsonNull ? string.Empty : item["afTransAmPr"].AsDouble.ToString() + "%";
                        sci.change_date = item["altDate"].IsBsonNull ? string.Empty : item["altDate"].AsString;
                        report.stock_changes.Add(sci);
                    }
                }
            }
        }
        #endregion

        #region 解析对外提供保证担保信息--年报
        /// <summary>
        /// 解析对外提供保证担保信息--年报
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportGuarantee(string responseData, Report report)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            if (document != null && document.Contains("recordsTotal") && document["recordsTotal"].AsInt32 > 0)
            {
                var arr = document.Contains("data") ? document["data"].AsBsonArray : new BsonArray();
                if (arr != null && arr.Any())
                {
                    foreach (BsonDocument item in arr)
                    {
                        ExternalGuarantee guarantee = new ExternalGuarantee();
                        guarantee.seq_no = report.external_guarantees.Count + 1;
                        guarantee.creditor = item["more"].IsBsonNull ? string.Empty : item["more"].AsString;
                        guarantee.debtor = item["mortgagor"].IsBsonNull ? string.Empty : item["mortgagor"].AsString;
                        guarantee.type = item["priClaSecKind"].IsBsonNull ? string.Empty : item["priClaSecKind"].AsString;
                        guarantee.type = guarantee.type == "1" ? "合同" : "其他";
                        guarantee.amount = item["priClaSecAm"].IsBsonNull ? string.Empty : item["priClaSecAm"].AsDouble.ToString() + "万元";
                        guarantee.period = string.Format("{0}~{1}", 
                            item["pefPerForm"].IsBsonNull ? string.Empty : item["pefPerForm"].AsString,
                            item["pefPerTo"].IsBsonNull ? string.Empty : item["pefPerTo"].AsString);
                        guarantee.guarantee_time = item["guaPeriod"].IsBsonNull ? string.Empty : item["guaPeriod"].AsString;
                        guarantee.guarantee_time = guarantee.guarantee_time == "1" ? "期间" : "未约定";
                        guarantee.guarantee_type = item["gaType"].IsBsonNull ? string.Empty : item["gaType"].AsString;
                        if (guarantee.guarantee_type == "1")
                        {
                            guarantee.guarantee_type = "一般保证";
                        }
                        else if (guarantee.guarantee_type == "2")
                        {
                            guarantee.guarantee_type = "连带保证";
                        }
                        else
                        {
                            guarantee.guarantee_type = "未约定";
                        }
                        guarantee.guarantee_scope = string.Empty;

                        report.external_guarantees.Add(guarantee);
                    }
                }
            }
            
        }
        #endregion

        #region 解析股东及出资信息
        /// <summary>
        /// 解析股东及出资信息
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseFinancialContribution(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            if (document != null && document.Contains("recordsTotal") && document["recordsTotal"].AsInt32 > 0)
            {
                var arr = document.Contains("data") ? document["data"].AsBsonArray : new BsonArray();
                if (arr != null && arr.Any())
                {
                    foreach (BsonDocument item in arr)
                    {
                        FinancialContribution fc = new FinancialContribution();
                        fc.seq_no = _enterpriseInfo.financial_contributions.Count + 1;
                        fc.investor_name = item["inv"].IsBsonNull ? string.Empty : item["inv"].AsString;
                        fc.total_should_capi = item["liSubConAm"].IsBsonNull ? string.Empty : item["liSubConAm"] == null ? string.Empty : item["liSubConAm"].AsDouble.ToString();
                        fc.total_real_capi = item["liAcConAm"].IsBsonNull ? string.Empty : item["liAcConAm"] == null ? string.Empty : item["liAcConAm"].AsDouble.ToString();
                        BsonArray should_Arr = item["imInvprodetailList"].IsBsonNull ? new BsonArray() : item["imInvprodetailList"].AsBsonArray;
                        BsonArray real_Arr = item["imInvactdetailList"].IsBsonNull ? new BsonArray() : item["imInvactdetailList"].AsBsonArray;
                        foreach (var should_item in should_Arr)
                        {
                            FinancialContribution.ShouldCapiItem sci = new FinancialContribution.ShouldCapiItem();
                            sci.should_capi = should_item["subConAm"].IsBsonNull ? string.Empty : should_item["subConAm"] == null ? string.Empty : should_item["subConAm"].AsDouble.ToString();
                            sci.should_invest_date = should_item["conDate"].IsBsonNull ? string.Empty : should_item["conDate"] == null ? string.Empty : should_item["conDate"].AsString;
                            sci.should_invest_type = should_item["conFormCN"].IsBsonNull ? string.Empty : should_item["conFormCN"] == null ? string.Empty : should_item["conFormCN"].AsString;
                            sci.public_date = should_item["publicDate"].IsBsonNull ? string.Empty : should_item["publicDate"] == null ? string.Empty : should_item["conDate"].AsString;
                            fc.should_capi_items.Add(sci);
                        }
                        foreach (var real_item in real_Arr)
                        {
                            FinancialContribution.RealCapiItem rci = new FinancialContribution.RealCapiItem();
                            rci.real_capi = real_item["acConAm"].IsBsonNull ? string.Empty : real_item["acConAm"] == null ? string.Empty : real_item["acConAm"].AsDouble.ToString();
                            rci.real_invest_date = real_item["conDate"].IsBsonNull ? string.Empty : real_item["conDate"]== null ? string.Empty : real_item["conDate"].AsString;
                            rci.real_invest_type = real_item["acConFormCn"].IsBsonNull ? string.Empty : real_item["acConFormCn"]== null ? string.Empty : real_item["acConFormCn"].AsString;
                            rci.public_date = real_item["publicDate"].IsBsonNull ? string.Empty : real_item["publicDate"] == null ? string.Empty : real_item["publicDate"].AsString;
                            fc.real_capi_items.Add(rci);
                        }
                        _enterpriseInfo.financial_contributions.Add(fc);
                    }
                }
            }
        }
        #endregion

        #region 解析股权变更
        void LoadAdnParseStockChange(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            if (document != null && document.Contains("recordsTotal") && document["recordsTotal"].AsInt32 > 0)
            {
                var arr = document.Contains("data") ? document["data"].AsBsonArray : new BsonArray();
                if (arr != null && arr.Any())
                {
                    foreach (BsonDocument item in arr)
                    {
                        StockChangeItem sci = new StockChangeItem();
                        sci.seq_no = _enterpriseInfo.stock_changes.Count + 1;
                        sci.name = item["inv"].IsBsonNull ? string.Empty : item["inv"].AsString;
                        sci.before_percent = item["beTransAmPr"].IsBsonNull ? string.Empty : item["beTransAmPr"].AsDouble.ToString() + "%";
                        sci.after_percent = item["afTransAmPr"].IsBsonNull ? string.Empty : item["afTransAmPr"].AsDouble.ToString() + "%"; 
                        sci.change_date = item["equAltDate"].IsBsonNull ? string.Empty : item["equAltDate"].AsString;
                        sci.public_date = item["publicDate"].IsBsonNull ? string.Empty : item["publicDate"].AsString;
                        _enterpriseInfo.stock_changes.Add(sci);
                    }
                }
            }
        }
        #endregion

        #region 解析行政许可信息
        void LoadAndParseLicence(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            if (document != null && document.Contains("recordsTotal") && document["recordsTotal"].AsInt32 > 0)
            {
                var arr = document.Contains("data") ? document["data"].AsBsonArray : new BsonArray();
                if (arr != null && arr.Any())
                {
                    foreach (BsonDocument item in arr)
                    {
                        LicenseInfo licenseInfo = new LicenseInfo();
                        licenseInfo.seq_no = _enterpriseInfo.licenses.Count + 1;
                        licenseInfo.number = item["licNO"].IsBsonNull ? string.Empty : item["licNO"].AsString;
                        licenseInfo.name = item["licNameCN"].IsBsonNull ? string.Empty : item["licNameCN"].AsString;
                        licenseInfo.start_date = item["valFrom"].IsBsonNull ? string.Empty : item["valFrom"].AsString;
                        licenseInfo.end_date = item["valTo"].IsBsonNull ? string.Empty : item["valTo"].AsString;
                        licenseInfo.department = item["licAnth"].IsBsonNull ? string.Empty : item["licAnth"].AsString;
                        licenseInfo.content = item["licItem"].IsBsonNull ? string.Empty : item["licItem"].AsString;
                        var status = item["pubType"].IsBsonNull ? string.Empty : item["pubType"].AsString;
                        if (!string.IsNullOrWhiteSpace(status))
                        {
                            if (status == "1")
                            {
                                licenseInfo.status = "变更";
                            }
                            else if (status == "2")
                            {
                                licenseInfo.status = "注销";
                            }
                            else if (status == "3")
                            {
                                licenseInfo.status = "被吊销";
                            }
                            else if (status == "4")
                            {
                                licenseInfo.status = "撤销";
                            }
                            else
                            {
                                licenseInfo.status = "有效";
                            }
                        }
                        else
                        {
                            licenseInfo.status = "有效";
                        }
                        _enterpriseInfo.licenses.Add(licenseInfo);
                    }
                }
            }
        }
        #endregion


        private  String CST2Local(string value)
        {

            string[] date = value.Split(new char[] { ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
            //生成本地日期字符串格式,GMT代表根据本地时区日期计算
            string datestr = string.Format("{0}, {1} {2} {3} {4}:{5}:{6} GMT", date[0], date[2], date[1], date[7], date[3], date[4], date[5]);
            DateTime dtt = Convert.ToDateTime(datestr);//转换成本地日期

            dtt = TimeZoneInfo.ConvertTimeToUtc(dtt);// 不加会相差8个小时
            //实际日期就出来了，是    星期四, 2010-01-07 01:08:03
            string str56 = dtt.ToString("yyyy-MM-dd HH:mm:ss");
            return str56.Substring(0, 10);
        }

        
        /// <summary>
        /// 解析年报
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseReports(string responseData, EnterpriseInfo _enterpriseInfo)
        {

            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            List<Report> reportList = new List<Report>();

            HtmlNodeCollection tables = rootNode.SelectNodes("//table[@class='detailsList']");
            if (tables != null)
            {
                foreach (HtmlNode table in tables)
                {
                    string header = table.SelectNodes("./tr/th")[0].InnerText.Trim();
                    if (header.StartsWith("企业年报") || header.StartsWith("个体工商户年报"))
                    {
                        HtmlNodeCollection trList = table.SelectNodes("./tr");
                        List<HtmlNode> listReports = new List<HtmlNode>();
                        if (trList != null && trList.Count > 2)
                        {
                            for (int i = 2; i < trList.Count; i++)
                            {
                                listReports.Add(trList[i]);

                            }

                            Parallel.ForEach(listReports, report => LoadReports(report, reportList));
                        }
                    }
                }
            }
            _enterpriseInfo.reports = reportList;
        }

        void LoadReports(HtmlNode hd, List<Report> reportList)
        {
            try
            {
                var request = CreateRequest();
                Report report = new Report();
                HtmlNodeCollection tdList = hd.SelectNodes("./td");
                if (tdList.Count == 1) return;
                report.report_name = tdList[1].InnerText.Trim();
                string year = tdList[1].InnerText.Trim().Length > 4 ? tdList[1].InnerText.Trim().Substring(0, 4) : "";
                report.report_year = year;
                report.report_date = ConvertDate(tdList[2].InnerText.Trim());
                if (!reportsNeedToLoad.Any() || reportsNeedToLoad.Contains(report.report_year))
                {
                    var link = tdList[1].SelectSingleNode("./a");
                    if (link != null)
                    {
                        // 更新解析Detail页面需要参数
                        string href = link.Attributes["href"].Value;
                        if (Regex.Split(href, "fldNo=").Count() > 1)
                        {
                            string fldNo = Regex.Split(Regex.Split(href, "fldNo=")[1], "&")[0];
                            request.AddOrUpdateRequestParameter("fldNo", fldNo);
                            request.AddOrUpdateRequestParameter("year", report.report_year);
                            List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("report"));
                            if (responseList.Count > 0)
                            {
                                LoadAndParseReportDetail(responseList[0].Data, report);
                            }

                            reportList.Add(report);
                        }
                        else if (Regex.Split(href, "indNo=").Count() > 1)
                        {
                            string fldNo = Regex.Split(Regex.Split(href, "indNo=")[1], "&")[0];
                            request.AddOrUpdateRequestParameter("indNo", fldNo);
                            request.AddOrUpdateRequestParameter("year", report.report_year);
                            List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("reportInd"));
                            if (responseList.Count > 0)
                            {
                                LoadAndParseReportDetail(responseList[0].Data, report);
                            }

                            reportList.Add(report);
                        }
                    }
                }
                
            }
            catch (Exception ex)
            {
                _enterpriseInfo.reports.Clear();
                Console.WriteLine("Exception when LoadAndParseReport.." + ex.ToString());
                LogHelper.Error("Exception when LoadAndParseReport.." + ex.ToString());
            }
            
        }
        /// <summary>
        /// 解析年报Detail
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportDetail(string responseData, Report report)
        {
            var request = CreateRequest();
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            HtmlNodeCollection tables = rootNode.SelectNodes("//table[@class='detailsList']");
            if (tables != null)
            {
                foreach (HtmlNode table in tables)
                {
                    if (table.InnerText.Contains("企业基本信息"))
                    {
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
                                        case "企业通信地址":
                                            report.address = tdList[i].InnerText.Trim();
                                            break;
                                        case "邮政编码":
                                            report.zip_code = tdList[i].InnerText.Trim();
                                            break;
                                        case "电子邮箱":
                                            report.email = tdList[i].InnerText.Trim();
                                            break;
                                        case "企业是否有投资信息或购买其他公司股权":
                                        case "企业是否有对外投资设立企业信息":
                                            report.if_invest = "否";
                                            break;
                                        case "是否有网站或网店":
                                        case "是否有网站或网点":
                                            report.if_website = "否";
                                            break;
                                        case "企业经营状态":
                                            report.status = tdList[i].InnerText.Trim();
                                            break;
                                        case "从业人数":
                                            report.collegues_num = tdList[i].InnerText.Trim();
                                            break;
                                        case "有限责任公司本年度是否发生股东股权转让":
                                            report.if_equity = "否";
                                            break;
                                        case "经营者姓名":
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
                    else if (table.InnerText.Contains("生产经营情况") || table.InnerText.Contains("企业资产状况信息"))
                    {
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
                                        case "资产总额":
                                            report.total_equity = tdList[i].InnerText.Trim();
                                            break;
                                        case "负债总额":
                                            report.debit_amount = tdList[i].InnerText.Trim();
                                            break;
                                        case "销售总额":
                                        case "营业总收入":
                                        case "营业额或营业收入":
                                            report.sale_income = tdList[i].InnerText.Trim();
                                            break;
                                        case "主营业务收入":
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
                }
            }


            // 解析出 reportNo，用于解析json
            var reportNoIndex = responseData.IndexOf("webReportNo");
            string webReportNo = responseData.Substring(reportNoIndex + 15, 26);
            report.ex_id = webReportNo;
            request.AddOrUpdateRequestParameter("webReportNo", report.ex_id);
            request.AddOrUpdateRequestParameter("year", report.report_year);
            List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("reportDetail"));
        }
        
        

        /// <summary>
        /// 年报-修改记录
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseReportUpdateRecord(string responseData, Report report)
        {
            responseData = responseData.Replace("{\"pagination\":", "");
            responseData = responseData.Substring(0, responseData.Length - 1);
            var urPagination = JsonConvert.DeserializeObject<UR_Pagination>(responseData);
            Utility.ClearNullValue<UR_Pagination>(urPagination);
            List<UpdateRecord> urLst = new List<UpdateRecord>();
            if (urPagination != null)
            {
                foreach (UR_Detail item in urPagination.dataList)
                {
                    UpdateRecord ur = new UpdateRecord();
                    Utility.ClearNullValue<UR_Detail>(item);
                    ur.seq_no = urLst.Count + 1;
                    ur.update_item = item.modItemName;
                    ur.update_date = UR_Detail.dateTimeTransfer(item.modDate);
                    string before = null;
                    string after = null;
                    FormatReportChange(item.modItem, ref before, ref after, item.modContentBefore, item.modContentAfter);
                    ur.before_update = before==null?Convert.ToString(item.modContentBefore):before;
                    string result = after == null ? item.modContentAfter : after;
                    ur.after_update = string.IsNullOrWhiteSpace(result) ? "此项已删除" : result;
                    urLst.Add(ur);
                }
            }
            report.update_records = urLst;
        }

        private void LoadAndParseOthersLicenseInfo(string response)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(response);
            HtmlNode rootNode = document.DocumentNode;
            List<LicenseInfo> licenses = new List<LicenseInfo>();
            HtmlNode table = rootNode.SelectSingleNode("//table[@class='detailsList']");
            if(table != null)
            {
                var rows = table.SelectNodes("./tr");
                foreach(var row in rows)
                {
                    var cells = row.SelectNodes("./td");
                    if (cells != null && cells.Count > 8)
                    {
                        LicenseInfo lic = new LicenseInfo();
                        lic.seq_no = _enterpriseInfo.licenses.Count + licenses.Count + 1;
                        lic.name = cells[2].InnerText;
                        lic.number = cells[1].InnerText;
                        lic.start_date = cells[3].InnerText;
                        lic.end_date = cells[4].InnerText;
                        lic.department = cells[5].InnerText;
                        lic.content = cells[6].InnerText;
                        lic.status = cells[7].InnerText;
                        licenses.Add(lic);
                    }
                }

            }
            _enterpriseInfo.licenses.AddRange(licenses);
        }

        private void FormatReportChange(string item, ref string before, ref string after, string beforeContent, string afterConetent)
        {
            if (item == "guarDateStart" || item == "guarDateEnd" || item == "conInfoPayDate" || item == "conInfoActDate" || item == "stockChangeDate")
            {
                if (beforeContent != "")
                {
                    before = ConvertDate(beforeContent);
                }
                if (afterConetent != "")
                {
                    after = ConvertDate(afterConetent);
                }
            }
            if (item == "conInfoInvForm" || item == "conInfoActForm")
            {
                before = GetInvestTypeNameByCode(beforeContent);
                after = GetInvestTypeNameByCode(afterConetent);
            }
            if (item == "guarRange")
            {
                before = GetRangeListNameByCode(beforeContent);
                after = GetRangeListNameByCode(afterConetent);
            }
            if (item == "guarCreditType")
            {
                before = GetGuarCreditTypeListNameByCode(beforeContent);
                after = GetGuarCreditTypeListNameByCode(afterConetent);
            }
            if (item == "guarType")
            {
                before = GetGuarTypeListNameByCode(beforeContent);
                after = GetGuarTypeListNameByCode(afterConetent);
            }
            if (item == "guarPeriod")
            {
                before = GetGuarPeriodListNameByCode(beforeContent);
                after = GetGuarPeriodListNameByCode(afterConetent);
            }
            if (item == "busWebType")
            {
                if (beforeContent == "0")
                {
                    before = "网站";
                }
                if (beforeContent == "1")
                {
                    before = "网 店 ";
                }
                if (afterConetent == "0")
                {
                    after = "网站";
                }
                if (afterConetent == "1")
                {
                    after = "网店";
                }
            }
        }


        private string investTypeTransfer(string code)
        {
            string result = "";
            var rangeArr = code.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var indiviualCode in rangeArr)
            {
                result += GetInvestTypeNameByCode(indiviualCode) + ",";
            }
            return result.TrimEnd(',');
        }

        private string GetInvestTypeNameByCode(string code)
        {
            switch (code)
            {
                case "1":
                    return "货币";
                case "2":
                    return "实物";
                case "3":
                    return "知识产权";
                case "4":
                    return "债权";
                case "6":
                    return "土地使用权";
                case "7":
                    return "股权";
                case "9":
                    return "其他";
            }
            return string.Empty;

        }

        private string GetConStateListNameByCode(string code)
        {
            switch (code)
            {
                case "1":
                    return "筹建";
                case "2":
                    return "投产开业";
                case "3":
                    return "停业";
                case "4":
                    return "清算";
            }
            return string.Empty;
        }

        private string GetGuarCreditTypeListNameByCode(string code)
        {
            switch (code)
            {
                case "1":
                    return "合同";
                case "2":
                    return "其他";
            }
            return string.Empty;
        }

        private string GetGuarPeriodListNameByCode(string code)
        {
            switch (code)
            {
                case "1":
                    return "期间";
                case "2":
                    return "未约定";
            }
            return string.Empty;
        }

        private string GetGuarTypeListNameByCode(string code)
        {
            switch (code)
            {
                case "1":
                    return "一般保证";
                case "2":
                    return "连带保证";
                case "3":
                    return "未约定";
            }
            return string.Empty;
        }

        private string GetRangeListNameByCode(string code)
        {
            switch (code)
            {
                case "1":
                    return "主债权";
                case "2":
                    return "利息";
                case "3":
                    return "违约金";
                case "4":
                    return "损害赔偿金";
                case "5":
                    return "实现债权的费用";
                case "6":
                    return "其他约金";
            }
            return string.Empty;
        }

        private string dateTimeTransfer(ConInfoActDate date)
        {
            if (date == null) return "";
            DateTime dt1970 = new DateTime(1970, 1, 1);
            return dt1970.AddMilliseconds(date.time).ToLocalTime().ToString("yyyy-MM-dd");
        }

        private string dateTimeTransfer2(ConInfoPayDate date)
        {
            if (date == null) return "";
            DateTime dt1970 = new DateTime(1970, 1, 1);
            return dt1970.AddMilliseconds(date.time).ToLocalTime().ToString("yyyy-MM-dd");
        }
    }
}
