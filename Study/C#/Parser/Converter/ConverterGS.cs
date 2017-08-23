using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Reflection;
using System.Collections;
using System.Xml.Linq;
using System.Collections.Specialized;

using Newtonsoft.Json;
using iOubo.iSpider.Model;
using HtmlAgilityPack;
using iOubo.iSpider.Common;
using System.Configuration;
using System.Threading;

namespace iOubo.iSpider.Infrastructure.Parser
{
    public class ConverterGS : IConverter
    {
        DataRequest _request;
        RequestInfo _requestInfo;
        RequestXml _requestXml;
        EnterpriseInfo _enterpriseInfo = new EnterpriseInfo();
        List<AbnormalInfo> _abnormals = new List<AbnormalInfo>();
        List<CheckupInfo> _checkups = new List<CheckupInfo>();
        List<string> reportsNeedToLoad = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings.Get("ReportsNeedToLoad"))
            ? new List<string>() : ConfigurationManager.AppSettings.Get("ReportsNeedToLoad").Split(',').ToList();
        string _geetestUserName = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings.Get("GeeTestUserName")) ? "berta" : ConfigurationManager.AppSettings.Get("GeeTestUserName");
        string _geetestPwd = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings.Get("GeeTestPwd")) ? "PCyYerqyh0Ga58qWug10" : ConfigurationManager.AppSettings.Get("GeeTestPwd");
        string _enterpriseName = string.Empty;
        int _parallelCount = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings.Get("ParallelCount")) ? 10 : int.Parse(ConfigurationManager.AppSettings.Get("ParallelCount"));
        public class geetestrequest
        {
            public int success { get; set; }
            public string gt { get; set; }
            public string challenge { get; set; }
        }

        public class geetestresponse
        {
            public string status { get; set; }
            public string challenge { get; set; }
            public string validate { get; set; }
        }

        public SummaryEntity ProcessRequestAndParse(RequestInfo requestInfo)
        {
            if (requestInfo.Parameters.ContainsKey("name")) _enterpriseName = requestInfo.Parameters["name"];
            this._requestInfo = requestInfo;
            this._requestInfo.Headers = new NameValueCollection();
            this._requestInfo.Headers.Add("Accept-Language", "zh-CN,zh;q=0.8");
            this._request = new DataRequest(this._requestInfo);
            if (requestInfo.Parameters.ContainsKey("platform") && "LIST_API" == requestInfo.Parameters["platform"])
            {
                this._requestXml = new RequestXml(requestInfo.CurrentPath, requestInfo.Province + "_API");
            }
            else
            {
                this._requestXml = new RequestXml(requestInfo.CurrentPath, requestInfo.Province);
            }
            InitialEnterpriseInfo();
            RequestHandler handler = new RequestHandler();
            //解析基本信息：基本信息、股东信息、变更信息、主要人员信息、分支机构信息、经营异常信息、抽查检查信息
            List<ResponseInfo> responseList = _request.GetResponseInfo(_requestXml.GetRequestListByGroup("basic"));
            //var basicInfo = responseList.FirstOrDefault(p => p.Name == "gongshang");
            if (requestInfo.Parameters.ContainsKey("platform") && "LIST_API" == requestInfo.Parameters["platform"])
            {
                this.LoadAndParseBasic_API(responseList.First().Data);
            }
            else
            {
                Parallel.ForEach(responseList, new ParallelOptions { MaxDegreeOfParallelism = _parallelCount }, responseInfo => this.ParseResponse_Parallel(responseInfo));
            }
            

            //解析年报
            SummaryEntity summaryEntity = new SummaryEntity();
            summaryEntity.Enterprise = _enterpriseInfo;
            summaryEntity.Abnormals = _abnormals;
            summaryEntity.Checkups = _checkups;

            return summaryEntity;
        }

        private void InitialEnterpriseInfo()
        {
            _enterpriseInfo.province = this._requestInfo.Province;
            _enterpriseInfo.last_update_time = DateTime.Now;
            _enterpriseInfo.source = "Batch";
            if (this._requestInfo.Parameters.ContainsKey("platform"))
            {
                this._requestInfo.Parameters.Remove("platform");
            }
            _enterpriseInfo.parameters = this._requestInfo.Parameters;
        }

        #region 创建请求
        /// <summary>
        /// 创建请求
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

        #region 解析企业信息
        /// <summary>
        /// 解析企业信息
        /// </summary>
        /// <param name="responseInfoList"></param>
        private void ParseResponse(List<ResponseInfo> responseInfoList)
        {
            foreach (ResponseInfo responseInfo in responseInfoList)
            {
                switch (responseInfo.Name)
                {
                    case "gongshang":
                        this.LoadAndParseTab_Basic(responseInfo.Data);
                        break;
                    case "report":
                        this.LoadAndParseTab_Report(responseInfo.Data);
                        break;
                    default:
                        break;
                }
            }
        }

        #endregion

        #region 解析企业信息
        /// <summary>
        /// 解析企业信息
        /// </summary>
        /// <param name="responseInfoList"></param>
        private void ParseResponse_Parallel(ResponseInfo responseInfo)
        {
            switch (responseInfo.Name)
            {
                case "gongshang":
                    this.LoadAndParseTab_Basic(responseInfo.Data);
                    break;
                case "report":
                    this.LoadAndParseTab_Report(responseInfo.Data);
                    break;
                default:
                    break;
            }
        }

        #endregion

        #region 解析工商公示信息：基本信息、股东信息、变更信息、主要人员信息、分支机构信息、经营异常信息、抽查检查信息、动产抵押、股权出质登记
        /// <summary>
        /// 解析工商公示信息：基本信息、股东信息、变更信息、主要人员信息、分支机构信息、经营异常信息、抽查检查信息、动产抵押、股权出质登记
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseTab_Basic(string responseData)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            this.LoadAndParseBasicInfo(rootNode);
            HtmlNode.ElementsFlags.Remove("input");
            HtmlNode.ElementsFlags.Remove("form");
            Parallel.Invoke(new ParallelOptions { MaxDegreeOfParallelism = 10 },
                    () => this.LoadAndParsePartners(rootNode),
                    () => this.LoadAndParseEmployees(rootNode),
                    () => this.LoadAndParseBranches(rootNode),
                    () => this.LoadAndParseChangeRecords(rootNode),
                    () => this.LoadAndParseAbnormal_items(rootNode),
                    () => this.LoadAndParseCheckups(rootNode),
                    () => this.LoadAndParseMortgages(rootNode),
                    () => this.LoadAndParseEquityQuality(rootNode),
                    () => this.LoadAndParseAdministrativePunishment(rootNode),
                    () => this.LoadAndParseEnterprise(rootNode),
                    () => this.LoadAndParseTab_Report(responseData)
                    );
        }
        #endregion

        #region LoadAndParseBasicOnly
        void LoadAndParseBasic_API(string responseData)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            this.LoadAndParseBasicInfo(rootNode);
        }
        #endregion

        #region 解析年报信息

        private void LoadAndParseTab_Report(string responseData)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            this.LoadAndParseReports(rootNode);
        }
        #endregion

        #region 加载基本信息
        private void LoadAndParseBasicInfo(HtmlNode rootNode)
        {
             var outerHtml=rootNode.OuterHtml;
            var start = outerHtml.IndexOf("<div id=\"con_three_1\"");
            var end = outerHtml.IndexOf("<div id=\"con_three_2\"");
            if (start > 0 && end > 0 && end > start)
            {
                var html = outerHtml.Substring(start, end - start);
                if (!string.IsNullOrWhiteSpace(html))
                {
                    HtmlDocument document = new HtmlDocument();
                    document.LoadHtml(html);
                    HtmlNode rn = document.DocumentNode;
                    this.CheckMessageIsError(rootNode);
                    var div = rn.SelectSingleNode("//div[@id='basic_']");
                    if (div != null)
                    {
                        var dls = div.SelectNodes("./dl[@class='info_name']");
                        if (dls != null && dls.Any())
                        {
                            foreach (var dl in dls)
                            {
                                string title = string.Empty;
                                if (dl.SelectSingleNode("./dt") != null)
                                {
                                    title = dl.SelectSingleNode("./dt").InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace("•", "").Replace(" ", "").Replace(" ", "").Trim(new char[] { ' ', ':', '：' });
                                }
                                else
                                {
                                     title = dl.InnerText.Contains("经营范围") || dl.InnerText.Contains("业务范围") ? "经营范围" : string.Empty;
                                }
                                var value = dl.SelectSingleNode("./dd").InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Replace("经营范围：", "").Replace("•", "").Replace("业务范围：", "").Trim();
                                switch (title)
                                {
                                    case "注册号":
                                        _enterpriseInfo.reg_no = value;
                                        break;
                                    case "统一社会信用代码/注册号":
                                        value = value.Trim(new char[] { '无' });
                                        if (value.Contains("/"))
                                        {
                                            var arr = value.Split('/');
                                            if (arr.Length == 2)
                                            {
                                                var first = arr.First();
                                                var last0 = arr.Last();
                                                if (first.Length == 18)
                                                {
                                                    _enterpriseInfo.credit_no = first;
                                                }
                                                else
                                                {
                                                    _enterpriseInfo.reg_no = first;
                                                }
                                                if (last0.Length == 18)
                                                {
                                                    _enterpriseInfo.credit_no = last0;
                                                }
                                                else
                                                {
                                                    _enterpriseInfo.reg_no = last0;
                                                }
                                            }

                                        }
                                        else
                                        {
                                            if (value.Length == 18)
                                            {
                                                _enterpriseInfo.credit_no = value;
                                            }
                                            else
                                            {
                                                _enterpriseInfo.reg_no = value;
                                            }
                                        }
                                        break;
                                    case "统一社会信用代码":
                                        _enterpriseInfo.credit_no = value;
                                        break;
                                    case "名称":
                                    case "企业名称":
                                    case "个体户名称":
                                        _enterpriseInfo.name = value;
                                        break;
                                    case "类型":
                                        _enterpriseInfo.econ_kind = value;
                                        break;
                                    case "组成形式":
                                        _enterpriseInfo.type_desc = value;
                                        break;
                                    case "法定代表人":
                                    case "经营者":
                                    case "负责人":
                                    case "股东":
                                    case "执行事务合伙人":
                                    case "投资人":
                                        _enterpriseInfo.oper_name = value;
                                        break;
                                    case "注册资金":
                                    case "注册资本":
                                    case "成员出资总额":
                                        var match = Regex.Match(html,"regcapVal.*?\\)");
                                        if(match!=null&&match.Success)
                                        {
                                            string reg = match.Value.Replace("regcapVal", "").Replace("=", "").Replace("toDecimal6","").Replace("('", "").Replace("')", "").Trim();
                                            _enterpriseInfo.regist_capi = reg+value;
                                        }
                                        
                                        break;
                                    case "成立日期":
                                    case "注册日期":
                                    case "登记日期":
                                        _enterpriseInfo.start_date = value;
                                        break;
                                    case "经营(驻在)期限自":
                                    case "营业(驻在)期限自":
                                    case "合伙(驻在)期限自":
                                    case "经营期限自":
                                    case "营业期限自":
                                    case "合伙期限自":
                                        _enterpriseInfo.term_start = value.Replace("<!--2014-8-7修改，营业期限起大于等于至的不再显示-->", "");
                                        break;
                                    case "经营(驻在)期限至":
                                    case "营业(驻在)期限至":
                                    case "合伙(驻在)期限至":
                                    case "经营期限至":
                                    case "营业期限至":
                                    case "合伙期限至":
                                        _enterpriseInfo.term_end = value;
                                        break;
                                    case "登记机关":
                                        _enterpriseInfo.belong_org = value;
                                        break;
                                    case "核准日期":
                                        _enterpriseInfo.check_date = value;
                                        break;
                                    case "住所":
                                    case "经营场所":
                                    case "营业场所":
                                    case "主要经营场所":
                                        _enterpriseInfo.addresses.Add(new Address { name = "注册地址", address = value });
                                        break;
                                    case "登记状态":
                                        _enterpriseInfo.status = value;
                                        break;
                                    case "经营范围":
                                    case "业务范围":
                                        _enterpriseInfo.scope = value;
                                        break;
                                    case "吊销日期":
                                    case "注销日期":
                                        _enterpriseInfo.end_date = value;
                                        break;
                                    default:
                                        break;
                                }
                            }
                            var last = dls.Last();
                            var scope = last.InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace("•", "");
                            if (scope.Contains("经营范围"))
                            {
                                _enterpriseInfo.scope = scope.Replace("经营范围：", "");
                            }

                        }
                    }
                }
            }
            
        }

        #endregion

        #region 加载股东信息
        /// <summary>
        /// 加载股东信息
        /// </summary>
        /// <param name="rootNode"></param>
        public void LoadAndParsePartners(HtmlNode rootNode)
        {
            var request = this.CreateRequest();
            var table = rootNode.SelectSingleNode("//table[@id='invTab']");
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        Partner partner = new Partner();
                        partner.seq_no = _enterpriseInfo.partners.Count + 1;
                        partner.stock_name = tds[1].InnerText;
                        partner.stock_type = tds[2].InnerText;
                        partner.identify_type = tds[3].InnerText;
                        partner.identify_no = tds[4].InnerText;

                        var aNode = tds.Last().SelectSingleNode("./a");
                        if (aNode != null)
                        {
                            var onclick = aNode.Attributes.Contains("onclick") ? aNode.Attributes["onclick"].Value : string.Empty;
                            if (!string.IsNullOrWhiteSpace(onclick))
                            {
                                var arr = onclick.Split('\'');
                                if (arr != null && arr.Length > 2)
                                {
                                    request.AddOrUpdateRequestParameter("invid", arr[1]);
                                    var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("partner_detail"));
                                    if (responseList != null && responseList.Any())
                                    {
                                        this.LoadAndParsePartnerDetail(responseList.First().Data, partner);
                                    }
                                }
                            }
                        }
                        _enterpriseInfo.partners.Add(partner);
                    }
                }
            }

        }
        #endregion

        #region 加载股东详情
        /// <summary>
        /// 加载股东详情
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="partner"></param>
        void LoadAndParsePartnerDetail(string responseData, Partner partner)
        {
            HtmlDocument document = new HtmlDocument();

            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var stockTab2 = rootNode.SelectSingleNode("//table[@id='stockTab2']");
            var rjDetail = rootNode.SelectSingleNode("//table[@id='rjDetail']");
            var sjDetail = rootNode.SelectSingleNode("//table[@id='sjDetail']");
            if (stockTab2 != null)
            {
                HtmlNodeCollection trList = stockTab2.SelectNodes("./tr");
                foreach (var tr in trList)
                {
                    var thlist = tr.SelectNodes("./th");
                    var tdlist = tr.SelectNodes("./td");
                    if (thlist[0].InnerText.Contains("认缴"))
                    {
                        partner.total_should_capi = tdlist[0].InnerText.Replace("\r\n", "").Trim();
                    }
                    else if (thlist[0].InnerText.Contains("实缴"))
                    {
                        partner.total_real_capi = tdlist[0].InnerText.Replace("\r\n", "").Trim();
                    }
                }
            }
            
            if (rjDetail != null)
            {
                //认缴
                var trs = rjDetail.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        ShouldCapiItem sci = new ShouldCapiItem();
                        sci.invest_type = tds[0].InnerText;
                        sci.shoud_capi = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        sci.should_capi_date = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Trim();
                        partner.should_capi_items.Add(sci);
                    }
                }
            }
            if (sjDetail != null)
            {
                var trs = sjDetail.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        RealCapiItem rci = new RealCapiItem();
                        rci.invest_type = tds[0].InnerText;
                        rci.real_capi = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        rci.real_capi_date = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Trim(); ;
                        partner.real_capi_items.Add(rci);
                    }
                }
            }
            if(stockTab2!=null)
            {
                var trs = stockTab2.SelectNodes("./tr");
                foreach (var tr in trs)
                {
                    var th = tr.SelectSingleNode("./th");
                    var td = tr.SelectSingleNode("./td");
                    if (th.InnerText.Contains("认缴出资额") && partner.should_capi_items.Count == 0)
                    {
                        ShouldCapiItem sci = new ShouldCapiItem();

                        sci.shoud_capi = td.InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                       
                        partner.should_capi_items.Add(sci);

                    }
                    else if (th.InnerText.Contains("实缴出资额") && partner.real_capi_items.Count == 0)
                    {
                        RealCapiItem rci = new RealCapiItem();
                        rci.real_capi =td.InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        partner.real_capi_items.Add(rci);
                    }
                }
            
            }
           
        }
        #endregion

        #region 加载主要人员
        /// <summary>
        /// 加载主要人员
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseEmployees(HtmlNode rootNode)
        {
            var divs = rootNode.SelectNodes("//div[@class='theme_cont']/div[@id='con_three_1']/div/div[@id='zyry']/div/div[@id='per270']");
            if (divs != null && divs.Any())
            {
                foreach (var div in divs)
                {
                    var ps = div.SelectNodes("./p");
                    Employee employee = new Employee();
                    employee.name = ps.First().InnerText;
                    employee.job_title = ps.Last().InnerText;
                    employee.seq_no = _enterpriseInfo.employees.Count + 1;
                    _enterpriseInfo.employees.Add(employee);
                }
            }

        }
        #endregion

        #region 加载分支机构
        /// <summary>
        /// 加载分支机构
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseBranches(HtmlNode rootNode)
        {
            var divs = rootNode.SelectNodes("//div[@class='theme_cont']/div[@id='con_three_1']/div/div[@id='fzjg_warp']/div[@id='fzjg308']");
            if (divs != null && divs.Any())
            {
                foreach (var div in divs)
                {
                    var ps = div.SelectNodes("./p");
                    Branch branch = new Branch();
                    branch.seq_no = _enterpriseInfo.branches.Count + 1;
                    branch.name = ps[0].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                    branch.reg_no = ps[1].InnerText.Replace("· ", "").Replace("统一社会信用代码/注册号：", "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                    branch.belong_org = ps[2].InnerText.Replace("· ", "").Replace("登记机关：", "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                    _enterpriseInfo.branches.Add(branch);
                }
            }
        }
        #endregion

        #region 加载变更记录
        /// <summary>
        /// 加载变更记录
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseChangeRecords(HtmlNode rootNode)
        {
            var outerHtml=rootNode.OuterHtml;
            var index = outerHtml.IndexOf("<table id=\"changeTab\"");
            if (index > 0)
            {
                var html = outerHtml.Substring(index);
                var endhtml = html.Substring(0, html.IndexOf("</table>") + 8);
                if (!string.IsNullOrWhiteSpace(endhtml))
                {
                    HtmlDocument document = new HtmlDocument();
                    document.LoadHtml(endhtml);
                    HtmlNode rn = document.DocumentNode;
                    var table = rn.SelectSingleNode("//table[@id='changeTab']");
                    if (table != null)
                    {
                        var trs = table.SelectNodes("./tr");
                        if (trs != null && trs.Any())
                        {
                            trs.Remove(0);
                            foreach (var tr in trs)
                            {
                                var tds = tr.SelectNodes("./td");
                                if (tds.Count > 4)
                                {
                                    ChangeRecord changerecord = new ChangeRecord();
                                    changerecord.seq_no = _enterpriseInfo.changerecords.Count + 1;
                                    changerecord.change_item = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    changerecord.before_content = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Trim();
                                    changerecord.after_content = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Trim();
                                    changerecord.change_date = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    _enterpriseInfo.changerecords.Add(changerecord);
                                }

                            }
                        }
                    }
                }
            }
        }

        #endregion

        #region 加载行政许可
        void LoadAndParseLicenseInfo(HtmlNode rootNode)
        {
            LicenseInfo license = new LicenseInfo();

        }
        #endregion

        #region 加载经营异常
        /// <summary>
        /// 加载经营异常
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseAbnormal_items(HtmlNode rootNode)
        {
            var table = rootNode.SelectSingleNode("//div[@class='theme_cont']/div[@id='con_three_4']/table[@id='excpTab']");
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds.Count == 7)
                        {
                            AbnormalInfo abnormalInfo = new AbnormalInfo();
                            abnormalInfo.name = _enterpriseInfo.name;
                            abnormalInfo.reg_no = _enterpriseInfo.reg_no;
                            abnormalInfo.province = _enterpriseInfo.province;
                            abnormalInfo.in_reason = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            abnormalInfo.in_date = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            abnormalInfo.department = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            abnormalInfo.out_reason = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            abnormalInfo.out_date = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            _abnormals.Add(abnormalInfo);
                        }
                    }
                }
            }

        }
        #endregion

        #region 加载抽查检查
        void LoadAndParseCheckups(HtmlNode rootNode)
        {
            var table = rootNode.SelectSingleNode("//div[@class='theme_cont']/div[@id='con_three_1']/div/div[@id='check_warp']/table[@id='checkTab']");
            if (table != null)
            {
                var trs = table.SelectNodes("./tbody/tr");
                if (trs != null && trs.Any())
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds.Count == 5)
                        {
                            CheckupInfo checkupInfo = new CheckupInfo();
                            checkupInfo.name = _enterpriseInfo.name;
                            checkupInfo.reg_no = _enterpriseInfo.reg_no;
                            checkupInfo.province = _enterpriseInfo.province;
                            checkupInfo.department = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            checkupInfo.type = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            checkupInfo.date = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            checkupInfo.result = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");

                            _checkups.Add(checkupInfo);
                        }
                    }
                }
            }

        }
        #endregion

        #region 加载动产抵押
        /// <summary>
        /// 加载动产抵押
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseMortgages(HtmlNode rootNode)
        {
            var request = this.CreateRequest();
            var table = rootNode.SelectSingleNode("//div[@class='theme_cont']/div[@id='con_three_1']/div/div[@id='zyry']/table[@id='moveTab']");
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds.Count == 8)
                        {
                            MortgageInfo item = new MortgageInfo();
                            item.seq_no = _enterpriseInfo.mortgages.Count + 1;
                            item.number = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            item.date = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            item.department = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            item.amount = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            item.status = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            item.public_date = tds[6].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            var aNode = tds.Last().SelectSingleNode("./a");
                            if (aNode != null)
                            {
                                var onClick = aNode.Attributes["onclick"] == null ? "" : aNode.Attributes["onclick"].Value;
                                if (!string.IsNullOrWhiteSpace(onClick))
                                {
                                    var arr = onClick.Split('\'');
                                    request.AddOrUpdateRequestParameter("morreg_id", arr[1]);
                                    List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("mortgage_detail"));
                                    if (responseList != null && responseList.Any())
                                    {
                                        this.LoadAndParseMortgageDetail(responseList[0].Data, item);
                                    }
                                }
                            }
                            _enterpriseInfo.mortgages.Add(item);
                        }
                    }
                }
            }

        }
        #endregion

        #region 加载动产抵押详情
        /// <summary>
        /// 加载动产抵押详情
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="item"></param>
        void LoadAndParseMortgageDetail(string responseData, MortgageInfo item)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            var div = rootNode.SelectSingleNode("//div[@id='sifapanding']");
            if (div != null)
            {
                var tables = div.SelectNodes("./table");
                if (tables != null && tables.Any())
                {
                    foreach (var table in tables)
                    {
                        this.LoadAndParseMortgageDetail_Analysis(table,item);
                    }
                }
            }
        }
        #endregion

        #region 加载动产抵押详情
        void LoadAndParseMortgageDetail_Analysis(HtmlNode table,MortgageInfo item)
        {
            //this.mortgagees = new List<Mortgagee>();
            //this.guarantees = new List<Guarantee>();
            var span = table.SelectSingleNode("./preceding-sibling::span[1]");
            var val = span.InnerText;
            
            var trs = table.SelectNodes("./tbody/tr");
            if (trs != null && trs.Any())
            {
                if (val.Equals("抵押权人概况信息") || val.Equals("抵押物概况信息"))
                {
                    trs.Remove(0);
                }
                foreach (var tr in trs)
                {
                    var ths = tr.SelectNodes("./th");
                    var tds = tr.SelectNodes("./td");
                    if (val.Equals("抵押权人概况信息"))
                    {
                        Mortgagee mortgagee = new Mortgagee();
                        mortgagee.seq_no = item.mortgagees.Count + 1;
                        mortgagee.name = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        mortgagee.identify_type = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        mortgagee.identify_no = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        item.mortgagees.Add(mortgagee);
                    }
                    else if (val.Equals("被担保债权概况信息"))
                    {
                        for (int i=0; i<ths.Count;i++)
                        {
                            var title = ths[i].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Trim();
                            switch (title)
                            {
                                case "种类":
                                    item.debit_type = tds[i].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    break;
                                case "数额":
                                    item.debit_amount = tds[i].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    break;
                                case "担保的范围":
                                    item.debit_scope = tds[i].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    break;
                                case "债务人履行债务的期限":
                                    item.debit_period = tds[i].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Replace("&nbsp;", "");
                                    break;
                                case "备注":
                                    item.debit_remarks = tds[i].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    break;
                                default:
                                    break;
                            }
                        }
                    }
                    else if (val.Equals("抵押物概况信息"))
                    {
                        Guarantee guarantee = new Guarantee();
                        guarantee.seq_no = item.guarantees.Count + 1;
                        guarantee.name = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        guarantee.belong_to = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        guarantee.desc = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        guarantee.remarks = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        item.guarantees.Add(guarantee);
                    }
                }
            }
        }
        #endregion

        #region 加载股权出质
        /// <summary>
        /// 加载股权出质
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseEquityQuality(HtmlNode rootNode)
        {
            var table = rootNode.SelectSingleNode("//div[@class='theme_cont']/div[@id='con_three_1']/div/div[@id='zyry']/table[@id='stockTab']");
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds.Count == 11)
                        {
                            EquityQuality equityQuality = new EquityQuality();
                            equityQuality.seq_no = _enterpriseInfo.equity_qualities.Count + 1;
                            equityQuality.number = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            equityQuality.pledgor = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            equityQuality.pledgor_identify_no = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            equityQuality.pledgor_amount = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            equityQuality.pawnee = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            equityQuality.pawnee_identify_no = tds[6].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            equityQuality.date = tds[7].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            equityQuality.status = tds[8].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            equityQuality.public_date = tds[9].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            var aNode = tds.Last().SelectSingleNode("./a");
                            if (aNode != null)
                            {
                                var onClick = aNode.Attributes["onclick"] == null ? "" : aNode.Attributes["onclick"].Value;
                                if (!string.IsNullOrWhiteSpace(onClick))
                                {
                                    var arr = onClick.Split('\'');
                                    var request = CreateRequest();
                                    request.AddOrUpdateRequestParameter("imporgid", arr[1]);
                                    List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("equityquality_detail"));
                                    if (responseList != null && responseList.Any())
                                    {
                                        //LoadAndParseEquityQualityChangeItem(responseList[0].Data, equityQuality);
                                    }
                                }

                            }
                            _enterpriseInfo.equity_qualities.Add(equityQuality);
                        }
                    }
                }
            }

        }
        #endregion

        #region 加载行政处罚
        /// <summary>
        /// 加载行政处罚
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseAdministrativePunishment(HtmlNode rootNode)
        {
            var request = CreateRequest();
            var table = rootNode.SelectSingleNode("//div[@class='theme_cont']/div[@id='con_three_3']/table[@id='xzcfTab']");
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds.Count == 8)
                        {
                            AdministrativePunishment ap = new AdministrativePunishment();
                            ap.seq_no = _enterpriseInfo.administrative_punishments.Count + 1;
                            ap.name = _enterpriseInfo.name;
                            ap.reg_no = _enterpriseInfo.reg_no;
                            ap.oper_name = _enterpriseInfo.oper_name;
                            ap.number = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            ap.illegal_type = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            ap.department = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            ap.content = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Replace("收起","");
                            ap.date = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            ap.public_date = tds[6].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            var aNode = tds.Last().SelectSingleNode("./a");
                            if (aNode != null)
                            {
                                var onClick = aNode.Attributes["onclick"] == null ? "" : aNode.Attributes["onclick"].Value;
                                if (!string.IsNullOrWhiteSpace(onClick))
                                {
                                    var arr = onClick.Split('\'');
                                    request.AddOrUpdateRequestParameter("caseid", arr[1]);
                                    List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("ap_detail"));
                                    if (responseList != null && responseList.Any())
                                    {
                                        LoadAndParseAPDetail(responseList[0].Data, ap);
                                    }
                                }
                            }
                            _enterpriseInfo.administrative_punishments.Add(ap);
                        }
                    }
                }
            }
        }
        #endregion

        #region 加载行政处罚详情
        void LoadAndParseAPDetail(string responseData,AdministrativePunishment ap)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            var table = rootNode.SelectSingleNode("//table[@class='show-ws']");
            
            if (table != null)
            {
                ap.description = table.OuterHtml;
            }
           
            
        }
        #endregion

        #region 加载企业即时信息
        void LoadAndParseEnterprise(HtmlNode rootNode)
        {
            this.LoadAndParseFinancialContribution(rootNode);
            this.LoadAndParseStockChanges(rootNode);
            this.LoadAndParseLicenses(rootNode);
            this.LoadAndParseKnowledgeProperty(rootNode);

        }
        #endregion

        #region 股东及出资信息
        /// <summary>
        /// 股东及出资信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseFinancialContribution(HtmlNode rootNode)
        {
            var outerHtml = rootNode.OuterHtml;
            var index = outerHtml.IndexOf("<table id=\"gd_JSTab\"");
            if (index > 0)
            {
                var html = outerHtml.Substring(index);
                var endhtml = html.Substring(0, html.IndexOf("</table>") + 8);
                if (!string.IsNullOrWhiteSpace(endhtml))
                {
                    HtmlDocument document = new HtmlDocument();
                    document.LoadHtml(endhtml);
                    HtmlNode rn = document.DocumentNode;
                    var table = rn.SelectSingleNode("//table[@id='gd_JSTab']");
                    if (table != null)
                    {
                        var trs = table.SelectNodes("./tr");
                        if (trs != null && trs.Any())
                        {
                            trs.Remove(0);
                            trs.Remove(0);
                            foreach (var tr in trs)
                            {
                                var tds = tr.SelectNodes("./td");
                                if (tds.Count == 12)
                                {
                                    FinancialContribution fc = new FinancialContribution();
                                    fc.seq_no = _enterpriseInfo.financial_contributions.Count + 1;
                                    fc.investor_name = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    fc.total_should_capi = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    fc.total_real_capi = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    FinancialContribution.ShouldCapiItem sci = new FinancialContribution.ShouldCapiItem();
                                    sci.should_invest_type = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    sci.should_capi = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    sci.should_invest_date = tds[6].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    sci.public_date = tds[7].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    fc.should_capi_items.Add(sci);
                                    FinancialContribution.RealCapiItem rci = new FinancialContribution.RealCapiItem();
                                    rci.real_invest_type = tds[8].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    rci.real_capi = tds[9].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    rci.real_invest_date = tds[10].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    rci.public_date = tds[11].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    fc.real_capi_items.Add(rci);
                                    _enterpriseInfo.financial_contributions.Add(fc);
                                }
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region 股权变更信息
        /// <summary>
        /// 股权变更信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseStockChanges(HtmlNode rootNode)
        {
            var outerHtml = rootNode.OuterHtml;
            var index = outerHtml.IndexOf("<table id=\"gqChangeJSTab\"");
            if (index > 0)
            {
                var html = outerHtml.Substring(index);
                var endhtml = html.Substring(0, html.IndexOf("</table>") + 8);
                if (!string.IsNullOrWhiteSpace(endhtml))
                {
                    HtmlDocument document = new HtmlDocument();
                    document.LoadHtml(endhtml);
                    HtmlNode rn = document.DocumentNode;
                    var table = rn.SelectSingleNode("//table[@id='gqChangeJSTab']");
                    if (table != null)
                    {
                        var trs = table.SelectNodes("./tr");
                        if (trs != null && trs.Any())
                        {
                            trs.Remove(0);
                            foreach (var tr in trs)
                            {
                                var tds = tr.SelectNodes("./td");
                                if (tds.Count == 6)
                                {
                                    StockChangeItem item = new StockChangeItem();
                                    item.seq_no = _enterpriseInfo.stock_changes.Count + 1;
                                    item.name = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    item.before_percent = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    item.after_percent = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    item.change_date = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    item.public_date = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    _enterpriseInfo.stock_changes.Add(item);
                                }
                            }
                        }
                    }
                }
            }
            
        }
        #endregion

        #region 加载行政许可信息
        /// <summary>
        /// 行政许可信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseLicenses(HtmlNode rootNode)
        {
            var outerHtml = rootNode.OuterHtml;
            var index = outerHtml.IndexOf("<table id=\"penTab\"");
            if (index > 0)
            {
                var html = outerHtml.Substring(index);
                var endhtml = html.Substring(0, html.IndexOf("</table>") + 8);
                if (!string.IsNullOrWhiteSpace(endhtml))
                {
                    HtmlDocument document = new HtmlDocument();
                    document.LoadHtml(endhtml);
                    HtmlNode rn = document.DocumentNode;
                    var table = rn.SelectSingleNode("//table[@id='penTab']");
                    if (table != null)
                    {
                        var trs = table.SelectNodes("./tr");
                        if (trs != null && trs.Any())
                        {
                            trs.Remove(0);
                            foreach (var tr in trs)
                            {
                                var tds = tr.SelectNodes("./td");
                                if (tds.Count == 9)
                                {
                                    LicenseInfo item = new LicenseInfo();
                                    item.seq_no = _enterpriseInfo.licenses.Count + 1;
                                    item.number = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    item.name = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    item.start_date = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    item.end_date = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    item.department = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    item.content = tds[6].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    item.status = tds[7].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    _enterpriseInfo.licenses.Add(item);
                                }
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region 加载知识产权出质登记信息
        void LoadAndParseKnowledgeProperty(HtmlNode rootNode)
        {
            //var outerHtml = rootNode.OuterHtml;
            //var index = outerHtml.IndexOf("<table id=\"zscq_JSTab\"");
            //if (index > 0)
            //{
            //    var html = outerHtml.Substring(index);
            //    var endhtml = html.Substring(0, html.IndexOf("</table>") + 8);
            //    if (!string.IsNullOrWhiteSpace(endhtml))
            //    {
            //        HtmlDocument document = new HtmlDocument();
            //        document.LoadHtml(endhtml);
            //        HtmlNode rn = document.DocumentNode;
            //        var table = rn.SelectSingleNode("//table[@id='zscq_JSTab']");
            //        if (table != null)
            //        {
            //            var trs = table.SelectNodes("./tr");
            //            if (trs != null && trs.Any())
            //            {
            //                trs.Remove(0);
            //                foreach (var tr in trs)
            //                {
            //                    var tds = tr.SelectNodes("./td");
            //                }
            //            }
            //        }
            //    }
            //}
            
        }
        #endregion

        #region 加载行政处罚信息
        #endregion

        #region 加载年报信息
        void LoadAndParseReports(HtmlNode rootNode)
        {
            
            var table = rootNode.SelectSingleNode("//table[@id='anche_tab']");
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
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
        }
        #endregion

        #region 解析加载年报信息--并行
        void LoadAndParseReport_Parallel(HtmlNode tr)
        {
            var request = this.CreateRequest();
            var tds = tr.SelectNodes("./td");
            if (tds != null && tds.Count == 4)
            {
                Report report = new Report();
                report.report_name = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                report.report_year = report.report_name.Substring(0, 4);
                report.report_date = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");

                var aNode = tds.Last().SelectSingleNode("./a");
                if (aNode != null && aNode.Attributes.Contains("onclick") && !string.IsNullOrWhiteSpace(aNode.Attributes["onclick"].Value))
                {
                    var arr = aNode.Attributes["onclick"].Value.Split('\'');
                    request.AddOrUpdateRequestParameter("report_ancheId", arr[1]);
                    request.AddOrUpdateRequestParameter("report_entcate", arr[3]);
                    request.AddOrUpdateRequestParameter("ancheyear", arr[5]);
                    var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("report"));
                    if (responseList != null && responseList.Any())
                    {
                        HtmlDocument document = new HtmlDocument();
                        document.LoadHtml(responseList.First().Data);
                        HtmlNode rn = document.DocumentNode;
                        this.LoadAndParseReportInfo(rn, report);
                    }

                }
                _enterpriseInfo.reports.Add(report);
            }
        }
        #endregion

        #region 加载年报信息
        void LoadAndParseReportInfo(HtmlNode rootNode, Report report)
        {
            this.LoadAndParseBasic_Report(rootNode,report);
            this.LoadAndParseWebsites_Report(rootNode, report);
            this.LoadAndParsePartners_Report(rootNode, report);
            this.LoadAndParseInvests_Report(rootNode, report);
            this.LoadAndParseAssets_Report(rootNode, report);
            this.LoadAndParseStockChanges_Report(rootNode, report);
            this.LoadAndParseUpdateRecords(rootNode, report);
            this.LoadAndParseSheBao(rootNode, report);
        }
        #endregion

        #region 加载基本信息--年报
        /// <summary>
        /// 加载基本信息--年报
        /// </summary>
        /// <param name="rootNode"></param>
        /// <param name="report"></param>
        void LoadAndParseBasic_Report(HtmlNode rootNode,Report report)
        {
            var headDiv = rootNode.SelectSingleNode("//div[@class='headClass']/div[@class='anche_head']");
            if (headDiv != null) 
            {
                var span = headDiv.SelectSingleNode("./span");
                var p = headDiv.SelectSingleNode("./p");
                if (span != null)
                {
                    report.report_name = span.InnerText;
                }
                if (p != null)
                {
                    report.report_date = p.InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Replace("填报时间：", "");
                }
            }
            var div = rootNode.SelectSingleNode("//div[@id='basic_']");
            if (div != null)
            {
                if (div != null)
                {
                    var dls = div.SelectNodes("./dl[@class='info_name']");
                    if (dls != null && dls.Any())
                    {
                        foreach (var dl in dls)
                        {
                            var title = dl.SelectSingleNode("./dt").InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace("•", "").TrimEnd(new char[] { ' ', ':', '：' });
                            var value = dl.SelectSingleNode("./dd").InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Trim();
                            switch (title.Trim())
                            {
                                case "统一社会信用代码/注册号":
                                    value = value.Trim(new char[] { '无' });
                                    if (value.Contains("/"))
                                    {
                                        var arr = value.Split('/');
                                        if (arr.Length == 2)
                                        {
                                            if (arr.First().Length == 18)
                                            {
                                                report.credit_no = arr.First();
                                            }
                                            else
                                            {
                                                report.reg_no = arr.First();
                                            }

                                            if (arr.Last().Length == 18)
                                            {
                                                report.credit_no = arr.Last();
                                            }
                                            else
                                            {
                                                report.reg_no = arr.Last();
                                            }
                                        }

                                    }
                                    else
                                    {
                                        if (value.Length == 18)
                                        {
                                            report.credit_no = value;
                                        }
                                        else
                                        {
                                            report.reg_no = value;
                                        }
                                    }
                                    break;
                                case "注册号":
                                    report.reg_no = value;
                                    break;
                                case "企业名称":
                                case "名称":
                                case "个体户名称":
                                    report.name = value;
                                    break;
                                case "企业联系电话":
                                case "联系电话":
                                case "经营者联系电话":
                                    report.telephone = value;
                                    break;
                                case "企业通信地址":
                                    report.address = value;
                                    break;
                                case "邮政编码":
                                    report.zip_code = value;
                                    break;
                                case "电子邮箱":
                                case "企业电子邮箱":
                                    report.email = value;
                                    break;
                                case "企业是否有投资信息或购买其他公司股权":
                                case "企业是否有对外投资设立企业信息":
                                    report.if_invest = value;
                                    break;
                                case "是否有对外提供担保信息":
                                    report.if_external_guarantee = value;
                                    break;
                                case "是否有网站或网店":
                                    report.if_website = value;
                                    break;
                                case "企业经营状态":
                                    report.status = value;
                                    break;
                                case "从业人数":
                                    report.collegues_num = value;
                                    break;
                                case "有限责任公司本年度是否发生股东股权转让":
                                    report.if_equity = value;
                                    break;
                                case "经营者姓名":
                                    report.oper_name = value;
                                    break;
                                case "资金数额":
                                    report.reg_capi = value;
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

        #region 加载网站--年报
        /// <summary>
        /// 加载网站--年报
        /// </summary>
        /// <param name="rootNode"></param>
        /// <param name="report"></param>
        void LoadAndParseWebsites_Report(HtmlNode rootNode,Report report)
        {
            var divs = rootNode.SelectNodes("//div[@id='webInfo']");
            if (divs != null && divs.Any())
            {
                foreach (var div in divs)
                {
                    var ps = div.SelectNodes("./p");
                    if (ps != null && ps.Any() && ps.Count==3)
                    {
                        WebsiteItem item = new WebsiteItem();
                        item.seq_no = report.websites.Count + 1;
                        item.web_name = ps[0].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        item.web_type = ps[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Split('：')[1];
                        item.web_url = ps[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Split('：')[1];
                        report.websites.Add(item);
                    }
                }
            }
        }
        #endregion

        #region 加载股东及出资信息--年报
        /// <summary>
        /// 加载股东及出资信息--年报
        /// </summary>
        /// <param name="rootNode"></param>
        /// <param name="report"></param>
        void LoadAndParsePartners_Report(HtmlNode rootNode, Report report)
        {
            var outerHtml = rootNode.OuterHtml;
            var index = outerHtml.IndexOf("<table id=\"gdczAnrepTab\"");
            if (index > 0)
            {
                var html = outerHtml.Substring(index);
                var endhtml = html.Substring(0, html.IndexOf("</table>") + 8);
                if (!string.IsNullOrWhiteSpace(endhtml))
                {
                    HtmlDocument document = new HtmlDocument();
                    document.LoadHtml(endhtml);
                    HtmlNode rn = document.DocumentNode;
                    var table = rn.SelectSingleNode("//table[@id='gdczAnrepTab']");
                    if (table != null)
                    {
                        var trs = table.SelectNodes("./tbody/tr");
                        if (trs != null && trs.Any())
                        {
                            trs.Remove(0);
                            foreach (var tr in trs)
                            {
                                
                                var tds = tr.SelectNodes("./td");
                                if (tds.Count == 8)
                                {
                                    Partner item = new Partner();
                                    item.seq_no = report.partners.Count + 1;
                                    item.stock_name = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    ShouldCapiItem sItem = new ShouldCapiItem();
                                    sItem.shoud_capi = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    sItem.should_capi_date = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    sItem.invest_type = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    item.should_capi_items.Add(sItem);

                                    RealCapiItem rItem = new RealCapiItem();
                                    rItem.real_capi = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    rItem.real_capi_date = tds[6].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    rItem.invest_type = tds[7].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    item.real_capi_items.Add(rItem);
                                    report.partners.Add(item);
                                }
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region 加载对外投资信息--年报
        /// <summary>
        /// 加载对外投资信息--年报
        /// </summary>
        /// <param name="rootNode"></param>
        /// <param name="report"></param>
        void LoadAndParseInvests_Report(HtmlNode rootNode, Report report)
        {

            var divs = rootNode.SelectNodes("//div[@id='webInfo']");
            if (divs != null && divs.Any())
            {
                foreach (var div in divs)
                {
                    var ps = div.SelectNodes("./p");
                    if (ps != null && ps.Any() && ps.Count ==2)
                    {
                        InvestItem item = new InvestItem();
                        item.seq_no = report.invest_items.Count + 1;
                        item.invest_name = ps[0].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        item.invest_reg_no = ps[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Replace("·统一社会信用代码/注册号：","");

                        report.invest_items.Add(item);
                    }
                }
            }
           
        }
        #endregion

        #region 加载企业资产状况信息--年报
        void LoadAndParseAssets_Report(HtmlNode rootNode, Report report)
        {
            var outerHtml = rootNode.OuterHtml;
            var index = outerHtml.IndexOf("<table id=\"zczkId\"");
            if (index > 0)
            {
                var html = outerHtml.Substring(index);
                var endhtml = html.Substring(0, html.IndexOf("</table>") + 8);
                if (!string.IsNullOrWhiteSpace(endhtml))
                {
                    HtmlDocument document = new HtmlDocument();
                    document.LoadHtml(endhtml);
                    HtmlNode rn = document.DocumentNode;
                    var table = rn.SelectSingleNode("//table[@id='zczkId']");
                    if (table != null)
                    {
                        var trs = table.SelectNodes("./tbody/tr");
                        if (trs != null && trs.Any())
                        {
                            foreach (var tr in trs)
                            {
                                var ths = tr.SelectNodes("./th");
                                var tds = tr.SelectNodes("./td");
                                for (int i = 0; i < ths.Count; i++)
                                {
                                    var id = ths[i].Attributes["id"].Value;
                                    var val = tds[i].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    switch (id)
                                    {
                                        case "assets"://资产总额(单位：万元)
                                            report.total_equity = val;
                                            break;
                                        case "totequ"://所有者权益合计(单位：万元)
                                            report.profit_reta = val;
                                            break;
                                        case "vendinc"://营业总收入(单位：万元)
                                            report.sale_income = val;
                                            break;
                                        case "progro"://利润总额(单位：万元)
                                            report.profit_total = val;
                                            break;
                                        case "maibusinc"://主营业务收入(单位：万元)
                                            report.serv_fare_income = val;
                                            break;
                                        case "netinc"://净利润(单位：万元)
                                            report.net_amount = val;
                                            break;
                                        case "ratgro"://纳税总额(单位：万元)
                                            report.tax_total = val;
                                            break;
                                        case "liability"://负债总额(单位：万元)
                                            report.debit_amount = val;
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
            
        }
        #endregion

        #region 加载股权变更信息--年报
        /// <summary>
        /// 加载股权变更信息--年报
        /// </summary>
        /// <param name="rootNode"></param>
        /// <param name="report"></param>
        void LoadAndParseStockChanges_Report(HtmlNode rootNode, Report report)
        {
            var outerHtml = rootNode.OuterHtml;
            var index = outerHtml.IndexOf("<table id=\"gqAlertTab\"");
            if (index > 0)
            {
                var html = outerHtml.Substring(index);
                var endhtml = html.Substring(0, html.IndexOf("</table>") + 8);
                if (!string.IsNullOrWhiteSpace(endhtml))
                {
                    HtmlDocument document = new HtmlDocument();
                    document.LoadHtml(endhtml);
                    HtmlNode rn = document.DocumentNode;
                    var table = rn.SelectSingleNode("//table[@id='gqAlertTab']");
                    if (table != null)
                    {
                        var trs = table.SelectNodes("./tbody/tr");
                        if (trs != null && trs.Any())
                        {
                            trs.Remove(0);
                            foreach (var tr in trs)
                            {
                                var tds = tr.SelectNodes("./td");
                                if (tds.Count == 5)
                                {
                                    StockChangeItem item = new StockChangeItem();
                                    item.seq_no = report.stock_changes.Count + 1;
                                    item.name = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    item.before_percent = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    item.after_percent = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    item.change_date = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");                                    
                                    report.stock_changes.Add(item);
                                }
                            }
                        }
                    }
                }
            }
            
        }
        #endregion

        #region 加载修改记录--年报
        /// <summary>
        /// 加载修改记录--年报
        /// </summary>
        /// <param name="rootNode"></param>
        /// <param name="report"></param>
        void LoadAndParseUpdateRecords(HtmlNode rootNode, Report report)
        {
            var outerHtml = rootNode.OuterHtml;
            var index = outerHtml.IndexOf("<table id=\"modifyTab\"");
            if (index > 0)
            {
                var html = outerHtml.Substring(index);
                var endhtml = html.Substring(0, html.IndexOf("</table>") + 8);
                if (!string.IsNullOrWhiteSpace(endhtml))
                {
                    HtmlDocument document = new HtmlDocument();
                    document.LoadHtml(endhtml);
                    HtmlNode rn = document.DocumentNode;
                    var table = rn.SelectSingleNode("//table[@id='modifyTab']");
                    if (table != null)
                    {
                        var trs = table.SelectNodes("./tbody/tr");
                        if (trs != null && trs.Any())
                        {
                            trs.Remove(0);
                            foreach (var tr in trs)
                            {
                                var tds = tr.SelectNodes("./td");
                                if (tds.Count == 5)
                                {
                                    UpdateRecord item = new UpdateRecord();
                                    item.seq_no = report.update_records.Count + 1;
                                    item.update_item = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    item.before_update = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    item.after_update = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    item.update_date = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                    report.update_records.Add(item);
                                }
                            }
                        }
                    }
                }
            }
            
        }
        #endregion

        #region 加载社保信息--年报
        void LoadAndParseSheBao(HtmlNode rootNode, Report report)
        {
            var div = rootNode.SelectSingleNode("//div[@class='base_content']/div/div/div/div[@id='shebao']");
            if (div != null)
            {
                HtmlNodeCollection trList = div.SelectNodes("./table/tbody/tr");

                foreach (HtmlNode rowNode in trList)
                {
                    HtmlNodeCollection thList = rowNode.SelectNodes("./th");
                    HtmlNodeCollection tdList = rowNode.SelectNodes("./td");

                    if (thList != null && tdList != null)
                    {
                        if (thList.Count > tdList.Count)
                        {
                            thList.Remove(0);
                        }
                        for (int i = 0; i < thList.Count; i++)
                        {
                            switch (thList[i].InnerText.Replace("（单位：人）", "").Replace("(万元)", "").Trim())
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
        }
        #endregion

        private string retrieveContent(HtmlNode td)
        {
            string result = "";
            HtmlNodeCollection span = td.SelectNodes("./span");
            if (span == null)
            {
                result = td.InnerText.Trim();
            }
            else
            {
                
                for (int i = 0; i < span.Count; i++ )
                {
                    HtmlNode tag_a = span[i].SelectSingleNode("./a");
                    if ("收起更多" == tag_a.InnerText.Trim()) {
                        string tempText = span[i].InnerText.Trim();
                        result = tempText.Replace("收起更多", "").Trim();
                    }
                }
            }
            return result;
        }

        void CheckMessageIsError(HtmlNode rootNode)
        {
            var div = rootNode.SelectSingleNode("//div[@id='entNameFont']");
            if (div != null)
            {
                if (!div.InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace("&nbsp;", "").Replace(" ", "").Contains(_enterpriseName))
                {
                    throw new Exception("甘肃网站内容错乱");
                }
            }
        }
    }
}
