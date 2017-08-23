using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Reflection;
using System.Collections;
using System.Xml.Linq;
using Newtonsoft.Json;
using iOubo.iSpider.Model;
using HtmlAgilityPack;
using iOubo.iSpider.Common;
using System.Configuration;

namespace iOubo.iSpider.Infrastructure.Parser
{
    public class ConverterGX : IConverter
    {
        DataRequest _request;
        RequestInfo _requestInfo;
        RequestXml _requestXml;
        EnterpriseInfo _enterpriseInfo = new EnterpriseInfo();
        List<AbnormalInfo> _abnormals = new List<AbnormalInfo>();
        List<CheckupInfo> _checkups = new List<CheckupInfo>();
        List<string> reportsNeedToLoad = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings.Get("ReportsNeedToLoad"))
            ? new List<string>() : ConfigurationManager.AppSettings.Get("ReportsNeedToLoad").Split(',').ToList();

        string _enterpriseName = string.Empty;
        string _regno = string.Empty;
        string _http = "http://gx.gsxt.gov.cn";
        int _parallelCount = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings.Get("ParallelCount")) ? 10 : int.Parse(ConfigurationManager.AppSettings.Get("ParallelCount"));
        public SummaryEntity ProcessRequestAndParse(RequestInfo requestInfo)
        {
            if (requestInfo.Parameters.ContainsKey("name")) _enterpriseName = requestInfo.Parameters["name"];
            this._requestInfo = requestInfo;
            this._request = new DataRequest(requestInfo);
            if (requestInfo.Parameters.ContainsKey("platform") && "LIST_API" == requestInfo.Parameters["platform"])
            {
                this._requestXml = new RequestXml(requestInfo.CurrentPath, "GX" + "_API");
            }
            else
            {
                this._requestXml = new RequestXml(requestInfo.CurrentPath, "GX");
            }
            InitialEnterpriseInfo();
            List<ResponseInfo> responseList = _request.GetResponseInfo(_requestXml.GetRequestListByGroup("basic"));
            var basicInfo = responseList.First(p => p.Name == "basicInfo");
            if (basicInfo != null)
            {
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(basicInfo.Data);
                HtmlNode rootNode = document.DocumentNode;
                var urls = this.LoadAndParseIframes(rootNode);

                urls.AddRange(this.LoadAndParseTabs(responseList.First(p => p.Name == "tabs").Data));
                this.LoadAndParseBasicInfo(rootNode);
                if (!(requestInfo.Parameters.ContainsKey("platform") && "LIST_API" == requestInfo.Parameters["platform"]))
                {
                    responseList.AddRange(this.GetResponseInfo(urls));
                    Parallel.ForEach(responseList, new ParallelOptions { MaxDegreeOfParallelism = _parallelCount }, responseInfo => this.ParseResponse(responseInfo));
                }
                else
                {
                    if (this._requestInfo.Parameters.ContainsKey("platform"))
                    {
                        this._requestInfo.Parameters.Remove("platform");
                    }
                    _enterpriseInfo.parameters = this._requestInfo.Parameters;
                }
            }

            SummaryEntity summaryEntity = new SummaryEntity();
            summaryEntity.Enterprise = _enterpriseInfo;
            summaryEntity.Abnormals = _abnormals;
            summaryEntity.Checkups = _checkups;
            if (summaryEntity.Enterprise.administrative_punishments != null && summaryEntity.Enterprise.administrative_punishments.Any())
            {
                int i = 1;
                foreach (var item in summaryEntity.Enterprise.administrative_punishments)
                {
                    item.seq_no = i;
                    i++;
                }
            }
            return summaryEntity;
        }

        private void InitialEnterpriseInfo()
        {
            _enterpriseInfo.province = this._requestInfo.Province;
            _enterpriseInfo.last_update_time = DateTime.Now;
            _enterpriseInfo.source = "Batch";

        }

        #region GetResponseInfo
        /// <summary>
        /// GetResponseInfo
        /// </summary>
        /// <param name="urls"></param>
        /// <returns></returns>
        List<ResponseInfo> GetResponseInfo(List<string> urls)
        {
            List<RequestSetting> requests = new List<RequestSetting>();
            if (urls != null)
            {
                var http = "http://gx.gsxt.gov.cn";
                foreach (var url in urls)
                {
                    RequestSetting request = new RequestSetting() { Url = url, Method = "get", IsArray = "0" };

                    if (url.StartsWith(http + "/gjjbj/gjjQueryCreditAction!touzirenInfo.dhtml"))//股东
                    {
                        request.Name = "partnerInfo";
                        requests.Add(request);
                    }
                    else if (url.StartsWith(http + "/gjjbj/gjjQueryCreditAction!zyryFrame.dhtml"))//主要人员
                    {
                        request.Name = "employeeInfo";
                        request.Url = string.Format("http://gx.gsxt.gov.cn/gjjbj/gjjQueryCreditAction!zyryFrame.dhtml?flag=more&ent_id={0}&credit_ticket={1}",
                            _requestInfo.Parameters["entId"], _requestInfo.Parameters["credit_ticket"]);
                        requests.Add(request);
                    }
                    else if (url.StartsWith(http + "/gjjbj/gjjQueryCreditAction!fzjgFrame.dhtml"))//分支机构
                    {
                        request.Name = "branchInfo";
                        request.Url = string.Format("http://gx.gsxt.gov.cn/gjjbj/gjjQueryCreditAction!fzjgFrame.dhtml?flag=more&ent_id={0}&regno={1}&credit_ticket={2}",
                            _requestInfo.Parameters["entId"], _regno, _requestInfo.Parameters["credit_ticket"]);
                        requests.Add(request);
                    }
                    else if (url.StartsWith(http + "/gjjbj/gjjQueryCreditAction!xj_biangengFrame.dhtml"))//变更信息
                    {
                        request.Name = "changerecordInfo";
                        requests.Add(request);
                    }
                    else if (url.StartsWith(http + "/gjjbjTab/gjjTabQueryCreditAction!dcdyFrame.dhtml"))//动产抵押
                    {
                        request.Name = "mortgageInfo";
                        requests.Add(request);
                    }
                    else if (url.StartsWith(http + "/gdczdj/gdczdjAction!gdczdjFrame.dhtml"))//股权出质
                    {
                        request.Name = "equity_qualitityInfo";
                        requests.Add(request);
                    }
                    else if (url.StartsWith(http + "/gsgs/gsxzcfAction!xj_list_ccjcxx.dhtml"))//抽查检查
                    {
                        request.Name = "checkupInfo";
                        requests.Add(request);
                    }
                    else if (url.StartsWith(http + "/sfxzxx/sfxzxxAction!sfxz_list.dhtml"))//司法协助
                    {
                        request.Name = "judicial_freezeInfo";
                        requests.Add(request);
                    }
                    else if (url.StartsWith(http + "/gjjbj/gjjQueryCreditAction!qynbxxList.dhtml"))//企业年报
                    {
                        request.Name = "reportInfo";
                        requests.Add(request);
                    }
                    else if (url.StartsWith(http + "/newChange/newChangeAction!getTabForNB_new.dhtml") && url.Contains("flag_num=1") && url.Contains("urltag=15"))//股东及出资
                    {
                        request.Name = "financial_contributionInfo";
                        requests.Add(request);
                    }
                    else if (url.StartsWith(http + "/newChange/newChangeAction!getTabForNB_new.dhtml") && url.Contains("flag_num=2") && url.Contains("urltag=15"))//股权变更
                    {
                        request.Name = "stock_changeInfo";
                        requests.Add(request);
                    }
                    else if (url.StartsWith(http + "/newChange/newChangeAction!getTabForNB_new.dhtml") && url.Contains("flag_num=4") && url.Contains("urltag=15"))//知识产权出质
                    {
                        request.Name = "knowledge_propertyInfo";
                        requests.Add(request);
                    }
                    else if (url.StartsWith(http + "/newChange/newChangeAction!getTabForNB_new.dhtml") && url.Contains("urltag=15"))//行政处罚信息
                    {
                        request.Name = "administrative_punishmentInfo";
                        requests.Add(request);
                    }
                    else if (url.StartsWith(http + "/gdgq/gdgqAction!xj_qyxzcfFrame.dhtml") && url.Contains("urltag=14"))//行政处罚信息--工商
                    {
                        request.Name = "administrative_punishmentInfo2";
                        requests.Add(request);
                    }
                    else if (url.StartsWith(http + "/newChange/newChangeAction!getTabForNB_new.dhtml") && url.Contains("flag_num=3"))//行政许可
                    {
                        request.Name = "licenseInfo";
                        requests.Add(request);
                    }
                    else if (url.StartsWith(http + "/xzxk/xzxkAction!xj_qyxzxkFrame.dhtml") && url.Contains("urltag=7"))//行政许可--工商
                    {
                        request.Name = "licenseInfo2";
                        requests.Add(request);
                    }
                    else if (url.StartsWith(http + "/gsgs/gsxzcfAction!list_jyycxx.dhtml") && url.Contains("urltag=8"))//经营异常--工商
                    {
                        request.Name = "abnormal_itemInfo";
                        requests.Add(request);
                    }
                }
            }

            return _request.GetResponseInfo(requests);
        }
        #endregion

        #region 解析企业信息
        /// <summary>
        /// 解析企业信息
        /// </summary>
        /// <param name="responseInfoList"></param>
        private void ParseResponse(ResponseInfo responseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            HtmlNode.ElementsFlags.Remove("form");
            HtmlNode.ElementsFlags.Remove("input");
            document.LoadHtml(responseInfo.Data);
            HtmlNode rootNode = document.DocumentNode;

            switch (responseInfo.Name)
            {
                case "partnerInfo":
                    this.LoadAndParsePartnerInfo(rootNode);
                    break;
                case "employeeInfo":
                    this.LoadAndParseEmployeeInfo(rootNode);
                    break;
                case "branchInfo":
                    this.LoadAndParseBranchInfo(rootNode);
                    break;
                case "changerecordInfo":
                    this.LoadAndParseChangeRecordInfo(rootNode);
                    break;
                case "mortgageInfo":
                    this.LoadAndParseMortgageInfo(rootNode);
                    break;
                case "equity_qualitityInfo":
                    this.LoadAndParseEquityQualityInfo(rootNode);
                    break;
                case "checkupInfo":
                    this.LoadAndParseCheckupInfo(rootNode);
                    break;
                case "judicial_freezeInfo":
                    this.LoadAndParseJudicialFreezeInfo(rootNode);
                    break;
                case "reportInfo":
                    this.LoadAndParseReportInfo(rootNode);
                    break;
                case "financial_contributionInfo":
                    this.LoadAndParseFinancialContributionInfo(rootNode);
                    break;
                case "stock_changeInfo":
                    this.LoadAndParseStockChangeInfo(rootNode);
                    break;
                case "licenseInfo":
                    this.LoadAndParseLicenceInfo(rootNode);
                    break;
                case "licenseInfo2":
                    this.LoadAndParseLicenceInfo2(rootNode);
                    break;
                case "knowledge_propertyInfo":
                    this.LoadAndParseKnowledgePropertyInfo(rootNode);
                    break;
                case "administrative_punishmentInfo":
                    this.LoadAndParseAdministrativePunishment(rootNode);
                    break;
                case "administrative_punishmentInfo2":
                    this.LoadAndParseAdministrativePunishment2(rootNode);
                    break;
                case "abnormal_itemInfo":
                    this.LoadAndParseAbnormalInfo(rootNode);
                    break;
                default:
                    break;

            }
        }
        #endregion

        #region 解析基本信息
        /// <summary>
        /// 解析基本信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseBasicInfo(HtmlNode rootNode)
        {
            var div = rootNode.SelectSingleNode("//div[@class='qyqx-detail']");
            if (div != null)
            {
                var trs = div.SelectNodes("./table/tbody/tr");
                if (trs != null && trs.Any())
                {
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Any())
                        {
                            foreach (var td in tds)
                            {
                                if (td.SelectSingleNode("./strong") == null) continue;
                                var title = td.SelectSingleNode("./strong").InnerText;
                                var val = td.InnerText.Replace(title, "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                switch (title)
                                {
                                    case "注册号：":
                                    case "统一社会信用代码：":
                                    case "注册号/统一社会信用代码：":
                                        if (val.Length == 18)
                                        {
                                            _enterpriseInfo.credit_no = val;
                                        }
                                        else
                                        {
                                            if (this._requestInfo.Parameters.ContainsKey("credit_no") && this._requestInfo.Parameters["credit_no"].Length == 18)
                                            {
                                                _enterpriseInfo.credit_no = this._requestInfo.Parameters["credit_no"];
                                            }
                                            _enterpriseInfo.reg_no = val;
                                        }
                                        break;
                                    case "名称：":
                                    case "企业名称：":
                                        _enterpriseInfo.name = val;
                                        break;
                                    case "类型：":
                                        _enterpriseInfo.econ_kind = val;
                                        break;
                                    case "法定代表人：":
                                    case "负责人：":
                                    case "股东：":
                                    case "经营者：":
                                    case "执行事务合伙人：":
                                    case "投资人：":
                                        _enterpriseInfo.oper_name = val;
                                        break;
                                    case "注册资金：":
                                    case "注册资本：":
                                    case "成员出资总额：":
                                        _enterpriseInfo.regist_capi = val;
                                        break;
                                    case "成立日期：":
                                    case "登记日期：":
                                    case "注册日期：":
                                        _enterpriseInfo.start_date = val;
                                        break;
                                    case "营业期限自：":
                                    case "经营期限自：":
                                    case "合伙期限自：":
                                        _enterpriseInfo.term_start = val;
                                        break;
                                    case "营业期限至：":
                                    case "经营期限至：":
                                    case "合伙期限至：":
                                        _enterpriseInfo.term_end = val;
                                        break;
                                    case "登记机关：":
                                        _enterpriseInfo.belong_org = val;
                                        break;
                                    case "核准日期：":
                                        _enterpriseInfo.check_date = val;
                                        break;
                                    case "住所：":
                                    case "经营场所：":
                                    case "营业场所：":
                                    case "主要经营场所：":
                                        _enterpriseInfo.addresses.Add(new Address { name = "注册地址", address = val, postcode = "" });
                                        break;
                                    case "登记状态：":
                                        _enterpriseInfo.status = val;
                                        break;
                                    case "经营范围：":
                                    case "业务范围：":
                                        _enterpriseInfo.scope = val;
                                        break;
                                    case "吊销日期：":
                                    case "注销日期：":
                                        _enterpriseInfo.end_date = val;
                                        break;
                                    case "组成形式：":
                                        _enterpriseInfo.type_desc = val;
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

        #region 解析iframe获取参数
        /// <summary>
        /// 解析iframe获取参数
        /// </summary>
        /// <param name="rootNode"></param>
        List<string> LoadAndParseIframes(HtmlNode rootNode)
        {
            List<string> list = new List<string>();
            var http = "http://gx.gsxt.gov.cn";
            var iframes = rootNode.SelectNodes("//iframe");
            var lis = rootNode.SelectNodes("//div[@class='bgbox']/div[@class='container-qy2']/div[@class='bbox']/div[@class='center']/ul[@class='qy-kind main-ul']/li");
            if (iframes != null && iframes.Any())
            {
                foreach (var iframe in iframes)
                {
                    var src = iframe.Attributes["src"].Value;
                    if (!string.IsNullOrWhiteSpace(src) && !src.StartsWith("/gjjbj/gjjQueryCreditAction!qsxxFrame.dhtml") && !src.StartsWith("/gjjbj/gjjQueryCreditAction!sbzcxxFrame.dhtml"))
                    {

                        list.Add(string.Format("{0}{1}", http, src));
                    }
                    if (src.StartsWith("/gjjbj/gjjQueryCreditAction!fzjgFrame.dhtml") && src.Contains("regno="))
                    {
                        var arr = src.Split(new char[] { '&', '?' });
                        if (arr != null && arr.Any())
                        {
                            foreach (var item in arr)
                            {
                                if (item.StartsWith("regno="))
                                {
                                    _request.AddOrUpdateRequestParameter("regno", item.Split('=')[1]);
                                    _regno = item.Split('=')[1];
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            return list;
        }
        #endregion

        #region 解析iframe获取参数
        /// <summary>
        /// 解析iframe获取参数
        /// </summary>
        /// <param name="rootNode"></param>
        List<string> LoadAndParseTabs(string responseData)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            List<string> list = new List<string>();
            var lis = rootNode.SelectNodes("//div[@class='bgbox']/div[@class='container-qy2']/div[@class='bbox']/div[@class='center']/ul[@class='qy-kind main-ul']/li");
            if (lis != null && lis.Any())
            {
                foreach (var li in lis)
                {
                    var src = li.SelectSingleNode("./a").Attributes["href"].Value;
                    if (!string.IsNullOrWhiteSpace(src))
                    {

                        list.Add(string.Format("{0}{1}", _http, src));
                    }
                }
            }

            return list;
        }
        #endregion

        #region 解析股东信息
        /// <summary>
        /// 解析股东信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParsePartnerInfo(HtmlNode rootNode)
        {
            var request = this.CreateRequest();

            var table = rootNode.SelectSingleNode("//table[@class='table-result']");
            if (table != null)
            {
                this.LoadAndParsePartnerInfoByPage(table);

                var form = rootNode.SelectSingleNode("//form");
                if (form == null) return;
                var pagesDiv = form.SelectSingleNode("div[@class='pages']");
                if (pagesDiv == null) return;
                var input = pagesDiv.ChildNodes.FirstOrDefault(node => node.Name == "input" && node.Attributes.Contains("id") && node.Attributes["id"].Value == "pagescount");
                if (input == null) return;
                var pages = int.Parse(input.Attributes["value"].Value);
                request.AddOrUpdateRequestParameter("partnerInfobypage_url", form.Attributes["action"].Value);

                for (int i = 2; i <= pages; i++)
                {
                    request.AddOrUpdateRequestParameter("pageNos", i.ToString());
                    request.AddOrUpdateRequestParameter("pageNo", (i - 1).ToString());
                    var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("partnerInfobypage"));
                    if (responseList != null && responseList.Any())
                    {
                        HtmlDocument document = new HtmlDocument();
                        document.LoadHtml(responseList.First().Data);
                        var rd = document.DocumentNode;
                        var pageTable = rd.SelectSingleNode("//table[@class='table-result']");
                        this.LoadAndParsePartnerInfoByPage(pageTable);
                    }
                }
            }
        }
        #endregion

        #region 股东信息分页
        /// <summary>
        /// 股东信息分页
        /// </summary>
        /// <param name="table"></param>
        void LoadAndParsePartnerInfoByPage(HtmlNode table)
        {
            var requst = this.CreateRequest();
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        int isSeqNo;
                        if (tds != null && tds.Any() && tds.Count == 6)
                        {
                            Partner partner = new Partner();
                            partner.seq_no = _enterpriseInfo.partners.Count + 1;
                            partner.stock_name = tds[1].InnerText;
                            partner.stock_type = tds[2].InnerText;
                            partner.identify_type = tds[3].InnerText;
                            partner.identify_no = tds[4].InnerText;

                            var a = tds.Last().SelectSingleNode("./a");
                            if (a != null)
                            {
                                var onclick = a.Attributes.Contains("onclick") ? a.Attributes["onclick"].Value : string.Empty;
                                if (!string.IsNullOrWhiteSpace(onclick))
                                {
                                    var arr = onclick.Split('\'');
                                    if (arr != null && arr.Length > 3)
                                    {
                                        requst.AddOrUpdateRequestParameter("ent_id", arr[1]);
                                        requst.AddOrUpdateRequestParameter("chr_id", arr[3]);
                                        requst.AddOrUpdateRequestParameter("time", this.GetTimeLikeJS().ToString());
                                        var responseList = requst.GetResponseInfo(_requestXml.GetRequestListByName("partner_detail"));
                                        if (responseList != null && responseList.Any())
                                        {
                                            this.LoadAndParsePartnerDetailInfo(responseList.First().Data, partner);
                                        }
                                    }
                                }
                            }
                            _enterpriseInfo.partners.Add(partner);
                        }
                        else if (tds != null && tds.Any() && tds.Count == 5 && int.TryParse(tds.First().InnerText, out isSeqNo))
                        {
                            Partner partner = new Partner();
                            partner.seq_no = _enterpriseInfo.partners.Count + 1;
                            partner.stock_name = tds[1].InnerText;
                            partner.stock_type = tds[2].InnerText;
                            partner.identify_type = tds[3].InnerText;
                            partner.identify_no = tds[4].InnerText;
                            _enterpriseInfo.partners.Add(partner);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析股东详情
        /// <summary>
        /// 解析股东详情
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="partner"></param>
        void LoadAndParsePartnerDetailInfo(string responseData, Partner partner)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            var tables = rootNode.SelectNodes("//table[@class='table-result']");
            if (tables != null && tables.Any())
            {
                if (tables.Count > 0)
                {
                    var shouldTable = tables[0];
                    HtmlNodeCollection trList = shouldTable.SelectNodes("./tr");
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
                if (tables.Count > 1)
                {
                    var shouldTable = tables[1];
                    if (shouldTable != null)
                    {
                        var trs = shouldTable.SelectNodes("./tr");
                        if (trs != null && trs.Any())
                        {
                            trs.Remove(0);
                            foreach (var tr in trs)
                            {
                                var tds = tr.SelectNodes("./td");
                                ShouldCapiItem sci = new ShouldCapiItem();
                                sci.invest_type = tds[0].InnerText.Replace("&nbsp;", "");
                                sci.shoud_capi = tds[1].InnerText.Replace("&nbsp;", "");
                                sci.should_capi_date = tds[2].InnerText.Replace("&nbsp;", "");
                                if (!string.IsNullOrWhiteSpace(sci.invest_type) || !string.IsNullOrWhiteSpace(sci.shoud_capi) || !string.IsNullOrWhiteSpace(sci.should_capi_date))
                                {
                                    partner.should_capi_items.Add(sci);
                                }

                            }
                        }
                    }
                }
                if (tables.Count > 2)
                {
                    var realTable = tables[2];
                    if (realTable != null)
                    {
                        var trs = realTable.SelectNodes("./tr");
                        if (trs != null && trs.Any())
                        {
                            trs.Remove(0);
                            foreach (var tr in trs)
                            {
                                var tds = tr.SelectNodes("./td");
                                RealCapiItem rci = new RealCapiItem();
                                rci.invest_type = tds[0].InnerText.Replace("&nbsp;", "");
                                rci.real_capi = tds[1].InnerText.Replace("&nbsp;", "");
                                rci.real_capi_date = tds[2].InnerText.Replace("&nbsp;", "");
                                if (!string.IsNullOrWhiteSpace(rci.invest_type) || !string.IsNullOrWhiteSpace(rci.real_capi) || !string.IsNullOrWhiteSpace(rci.real_capi_date))
                                {
                                    partner.real_capi_items.Add(rci);
                                }

                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析主要人员
        /// <summary>
        /// 解析主要人员
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseEmployeeInfo(HtmlNode rootNode)
        {
            var divs = rootNode.SelectNodes("//div[@class='qyqx-detail']");
            if (divs != null && divs.Any())
            {
                foreach (var div in divs)
                {
                    var tables = div.SelectNodes("./table");
                    if (tables != null && tables.Any())
                    {
                        foreach (var table in tables)
                        {
                            var trs = table.SelectNodes("./tbody/tr");
                            if (trs != null && trs.Any() && trs.Count == 2)
                            {
                                Employee employee = new Employee();
                                employee.seq_no = _enterpriseInfo.employees.Count + 1;
                                employee.name = trs.First().InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                employee.job_title = trs.Last().InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", ""); ;
                                _enterpriseInfo.employees.Add(employee);
                            }
                        }
                    }
                }

            }
        }
        #endregion

        #region 解析分支机构
        /// <summary>
        /// 解析分支机构
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseBranchInfo(HtmlNode rootNode)
        {
            var divs = rootNode.SelectNodes("//div[@class='qyqx-detail']");
            if (divs != null && divs.Any())
            {
                foreach (var div in divs)
                {
                    var tables = div.SelectNodes("./table");
                    if (tables != null && tables.Any())
                    {
                        foreach (var table in tables)
                        {
                            var trs = table.SelectNodes("./tbody/tr");
                            if (trs != null && trs.Any() && trs.Count == 3)
                            {
                                Branch branch = new Branch();
                                branch.seq_no = _enterpriseInfo.branches.Count + 1;
                                branch.name = trs[0].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", ""); ;
                                branch.reg_no = trs[1].InnerText.Replace("· 统一社会信用代码/注册号：", "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                branch.belong_org = trs[2].InnerText.Replace("· 登记机关：", "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", ""); ;
                                _enterpriseInfo.branches.Add(branch);
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析变更信息
        /// <summary>
        /// 解析变更信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseChangeRecordInfo(HtmlNode rootNode)
        {
            var request = this.CreateRequest();
            var table = rootNode.SelectSingleNode("//table[@class='table-result']");
            if (table != null)
            {
                this.LoadAndParseChangeRecordInfoByPage(table);
                var form = rootNode.SelectSingleNode("//form");
                if (form == null) return;
                var pagesDiv = form.SelectSingleNode("div[@class='pages']");
                if (pagesDiv == null) return;
                var input = pagesDiv.ChildNodes.FirstOrDefault(node => node.Name == "input" && node.Attributes.Contains("id") && node.Attributes["id"].Value == "pagescount");
                if (input == null) return;
                var pages = int.Parse(input.Attributes["value"].Value);

                for (int i = 2; i <= pages; i++)
                {
                    request.AddOrUpdateRequestParameter("pageNos", i.ToString());
                    request.AddOrUpdateRequestParameter("pageNo", (i - 1).ToString());
                    var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("changerecordInfobypage"));
                    if (responseList != null && responseList.Any())
                    {
                        HtmlDocument document = new HtmlDocument();
                        document.LoadHtml(responseList.First().Data);
                        var rd = document.DocumentNode;
                        var pageTable = rd.SelectSingleNode("//table[@class='table-result']");
                        this.LoadAndParseChangeRecordInfoByPage(pageTable);
                    }
                }
            }
        }
        #endregion

        #region 解析变更信息--分页
        /// <summary>
        /// 解析变更信息--分页
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseChangeRecordInfoByPage(HtmlNode table)
        {
            var request = this.CreateRequest();
            if (table != null)
            {
                var trs = table.SelectNodes("./tbody/tr");
                if (trs != null && trs.Any())
                {

                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        ChangeRecord changeRecord = new ChangeRecord();
                        if (tds != null && tds.Any() && tds.Count == 5)
                        {

                            changeRecord.seq_no = _enterpriseInfo.changerecords.Count + 1;
                            changeRecord.change_item = tds[1].InnerText;
                            changeRecord.before_content = tds[2].InnerText;
                            changeRecord.after_content = tds[3].InnerText;
                            changeRecord.change_date = tds[4].InnerText;
                            _enterpriseInfo.changerecords.Add(changeRecord);

                        }
                        else if (tds != null && tds.Any() && tds.Count == 4 && tds[2].SelectSingleNode("./a") != null)
                        {
                            changeRecord.seq_no = _enterpriseInfo.changerecords.Count + 1;
                            changeRecord.change_item = tds[1].InnerText;
                            changeRecord.change_date = tds[3].InnerText;
                            var aNode = tds[2].SelectSingleNode("./a");
                            var onclick = aNode.Attributes.Contains("onclick") ? aNode.Attributes["onclick"].Value : string.Empty;
                            if (string.IsNullOrWhiteSpace(onclick)) continue;
                            var arr = onclick.Split('\'');
                            if (arr != null && arr.Length > 2)
                            {
                                request.AddOrUpdateRequestParameter("changerecord_detail_url", arr[1]);
                                var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("changerecord_detail"));
                                var category = 1;
                                category = arr[1].Contains("old_reg_his_id") ? 2 : 1;
                                if (responseList != null && responseList.Any())
                                {
                                    this.LoadAndParseChangeRecordDetail(changeRecord, responseList.First().Data, category);
                                }
                            }
                            _enterpriseInfo.changerecords.Add(changeRecord);
                        }

                    }
                }
            }
        }
        #endregion

        #region 解析变更信息详情
        void LoadAndParseChangeRecordDetail(ChangeRecord changeRecord, string responseData, int category)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNodeDetail = document.DocumentNode;
            if (changeRecord.change_item.Contains("董事") || changeRecord.change_item.Contains("理事") || changeRecord.change_item.Contains("经理") || changeRecord.change_item.Contains("监事"))
            {
                changeRecord.before_content = "（注：标有*标志的为法定代表人）\r\n";
                changeRecord.after_content = "（注：标有*标志的为法定代表人）\r\n";
            }
            if (category == 2)
            {
                HtmlNodeCollection tables = rootNodeDetail.SelectNodes("./table[@id='table-result']");
                if (tables == null)
                {
                    tables = rootNodeDetail.SelectNodes("./table[@class='table-result']");
                }
                if (tables != null && tables.Count == 3)
                {
                    HtmlNodeCollection detailTrList = tables[1].SelectNodes("./tr");
                    for (int i = 2; i < detailTrList.Count; i++)
                    {
                        HtmlNodeCollection detailTdList = detailTrList[i].SelectNodes("./td");
                        foreach (HtmlNode td in detailTdList)
                        {
                            changeRecord.before_content += td.InnerText.Trim() + " ";
                        }
                        changeRecord.before_content += "\r\n";
                    }
                    detailTrList = tables[2].SelectNodes("./tr");
                    for (int i = 2; i < detailTrList.Count; i++)
                    {
                        HtmlNodeCollection detailTdList = detailTrList[i].SelectNodes("./td");
                        foreach (HtmlNode td in detailTdList)
                        {
                            changeRecord.after_content += td.InnerText.Trim() + " ";
                        }
                        changeRecord.after_content += "\r\n";
                    }
                }

            }
            else if (category == 1)
            {
                HtmlNodeCollection tables = rootNodeDetail.SelectNodes("//table[@class='tableIdStyle']");
                if (tables != null && tables.Count == 3)
                {
                    changeRecord.before_content = tables[2].SelectNodes("./tr/td")[0].InnerText.Trim();
                    changeRecord.after_content = tables[2].SelectNodes("./tr/td")[1].InnerText.Trim();
                }
                else
                {
                    changeRecord.before_content = tables[1].SelectNodes("./tr/td")[0].InnerText.Trim();
                    changeRecord.after_content = tables[1].SelectNodes("./tr/td")[1].InnerText.Trim();
                }
            }

        }
        #endregion

        #region 解析动产抵押
        /// <summary>
        /// 解析动产抵押
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseMortgageInfo(HtmlNode rootNode)
        {
            var request = this.CreateRequest();
            var table = rootNode.SelectSingleNode("//table[@class='table-result']");
            if (table != null)
            {
                this.LoadAndParseMortgageInfoByPage(table);
                var input = rootNode.SelectSingleNode("//input[@id='pagescount']");
                if (input != null)
                {
                    var pages = int.Parse(input.Attributes["value"].Value);
                    request.AddOrUpdateRequestParameter("pageNos", pages.ToString());
                    for (int i = 2; i <= pages; i++)
                    {
                        request.AddOrUpdateRequestParameter("pageNo", i.ToString());
                        var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("mortgageInfobypage"));
                        if (responseList != null && responseList.Any())
                        {
                            HtmlDocument document = new HtmlDocument();
                            document.LoadHtml(responseList.First().Data);
                            var rd = document.DocumentNode;
                            var pageTable = rd.SelectSingleNode("//table[@class='table-result']");
                            this.LoadAndParseMortgageInfoByPage(pageTable);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析动产抵押--分页
        /// <summary>
        /// 解析动产抵押--分页
        /// </summary>
        /// <param name="table"></param>
        void LoadAndParseMortgageInfoByPage(HtmlNode table)
        {
            var requst = this.CreateRequest();
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Any() && tds.Count == 8)
                        {
                            MortgageInfo mortgageInfo = new MortgageInfo();
                            mortgageInfo.seq_no = _enterpriseInfo.mortgages.Count + 1;
                            mortgageInfo.number = tds[1].InnerText;
                            mortgageInfo.date = tds[2].InnerText;
                            mortgageInfo.department = tds[3].InnerText;
                            mortgageInfo.amount = tds[4].InnerText;
                            mortgageInfo.status = tds[5].InnerText;
                            mortgageInfo.public_date = tds[6].InnerText;
                            var a = tds.Last().SelectSingleNode("./a");
                            if (a != null)
                            {
                                var onclick = a.Attributes.Contains("onclick") ? a.Attributes["onclick"].Value : string.Empty;
                                if (!string.IsNullOrWhiteSpace(onclick))
                                {
                                    var arr = onclick.Split(new char[] { '\'', '?', '&' });
                                    if (arr != null && arr.Length == 5)
                                    {
                                        requst.AddOrUpdateRequestParameter("chr_id", arr[3].Split('=')[1]);
                                        var responseList = requst.GetResponseInfo(_requestXml.GetRequestListByGroup("mortgageInfo_detail"));
                                        if (responseList != null && responseList.Any())
                                        {
                                            foreach (var responseInfo in responseList)
                                            {
                                                switch (responseInfo.Name)
                                                {
                                                    case "mortgageInfo_detail_bdbzq":
                                                        this.LoadAndParseMortgageDetailInfo(responseInfo.Data, mortgageInfo);
                                                        break;
                                                    case "mortgageInfo_detail_dyqr":
                                                        this.LoadAndParseMortgage_DYQR(responseInfo.Data, mortgageInfo);
                                                        break;
                                                    case "mortgageInfo_detail_dyw":
                                                        this.LoadAndParseMortgage_DYW(arr[3].Split('=')[1], responseInfo.Data, mortgageInfo);
                                                        break;
                                                    default:
                                                        break;
                                                }
                                            }
                                            this.LoadAndParseMortgageDetailInfo(responseList.First().Data, mortgageInfo);
                                        }
                                    }
                                }
                            }
                            _enterpriseInfo.mortgages.Add(mortgageInfo);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析动产抵详情
        void LoadAndParseMortgageDetailInfo(string responseData, MortgageInfo mortgageInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            var tables = rootNode.SelectNodes("//div[@style='height:500px;overflow:scroll;overflow-x:hidden;']/div[@style='width:800px;height:600px;']/table");
            if (tables != null && tables.Count >= 2)
            {
                var table = tables[1];
                var trs = table.SelectNodes("./tr");
                foreach (HtmlNode rowNode in trs)
                {
                    HtmlNodeCollection thList = rowNode.SelectNodes("./th");
                    HtmlNodeCollection tdList = rowNode.SelectNodes("./td");

                    if (thList != null && tdList != null && thList.Count == tdList.Count)
                    {
                        for (int i = 0; i < thList.Count; i++)
                        {
                            switch (thList[i].InnerText.Trim())
                            {
                                case "种类":
                                    mortgageInfo.debit_type = tdList[i].InnerText.Trim();
                                    break;
                                case "数额":
                                    mortgageInfo.debit_amount = tdList[i].InnerText.Trim();
                                    break;
                                case "担保的范围":
                                    mortgageInfo.debit_scope = tdList[i].InnerText.Trim();
                                    break;
                                case "债务人履行债务的期限":
                                    mortgageInfo.debit_period = tdList[i].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Trim();
                                    break;
                                case "备注":
                                    mortgageInfo.debit_remarks = tdList[i].InnerText.Trim();
                                    break;
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析动产抵押详情--抵押权人
        /// <summary>
        /// 解析动产抵押详情--抵押权人
        /// </summary>
        /// <param name="mortgage"></param>
        /// <param name="responseData"></param>
        void LoadAndParseMortgage_DYQR(string responseData, MortgageInfo mortgage)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            var request = this.CreateRequest();
            var table = rootNode.SelectSingleNode("//table[@class='table-result']");
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Any() && tds.Count == 4)
                        {
                            Mortgagee mortgagee = new Mortgagee();
                            mortgagee.seq_no = mortgage.guarantees.Count + 1;
                            mortgagee.name = tds[1].InnerText;
                            mortgagee.identify_type = tds[2].InnerText;
                            mortgagee.identify_no = tds[3].InnerText;
                            mortgage.mortgagees.Add(mortgagee);
                        }

                    }
                }
            }
        }
        #endregion

        #region 解析动产抵押详情--抵押物分页
        /// <summary>
        /// 解析动产抵押详情--抵押物分页 
        /// </summary>
        /// <param name="table"></param>
        /// <param name="mortgage"></param>
        void LoadAndParseMortgageByPage_DYW(HtmlNode table, MortgageInfo mortgage)
        {
            if (table == null) return;
            var trs = table.SelectNodes("./tr");
            if (trs != null && trs.Any())
            {
                foreach (var tr in trs)
                {
                    var tds = tr.SelectNodes("./td");
                    if (tds != null && tds.Any() && tds.Count == 5)
                    {
                        Guarantee guarantee = new Guarantee();
                        guarantee.seq_no = mortgage.guarantees.Count + 1;
                        guarantee.name = tds[1].InnerText;
                        guarantee.belong_to = tds[2].InnerText;
                        guarantee.desc = tds[3].InnerText;
                        guarantee.remarks = tds[4].InnerText;
                        mortgage.guarantees.Add(guarantee);
                    }

                }
            }
        }
        #endregion

        #region 解析动产抵押详情--抵押物
        /// <summary>
        /// 解析动产抵押详情--抵押物
        /// </summary>
        /// <param name="mortgage"></param>
        /// <param name="responseData"></param>
        void LoadAndParseMortgage_DYW(string chr_id, string responseData, MortgageInfo mortgage)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            var request = this.CreateRequest();
            var table = rootNode.SelectSingleNode("//table[@class='table-result']");
            if (table != null)
            {
                this.LoadAndParseMortgageByPage_DYW(table, mortgage);

                var form = rootNode.SelectSingleNode("//form");
                if (form == null) return;
                var pagesDiv = form.SelectSingleNode("div[@class='pages']");
                if (pagesDiv == null) return;
                var input = pagesDiv.ChildNodes.FirstOrDefault(node => node.Name == "input" && node.Attributes.Contains("id") && node.Attributes["id"].Value == "pagescount");
                if (input == null) return;
                if (input != null)
                {
                    request.AddOrUpdateRequestParameter("chr_id", chr_id);
                    var pages = int.Parse(input.Attributes["value"].Value);

                    for (int i = 2; i <= pages; i++)
                    {
                        request.AddOrUpdateRequestParameter("pageNos", i.ToString());
                        request.AddOrUpdateRequestParameter("pageNo", (i - 1).ToString());
                        var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("mortgageInfo_detail_dywbypage"));

                        if (responseList != null && responseList.Any())
                        {
                            HtmlDocument inner_document = new HtmlDocument();
                            inner_document.LoadHtml(responseList.First().Data);
                            var rd = inner_document.DocumentNode;
                            var pageTable = rd.SelectSingleNode("//table[@class='table-result']");
                            this.LoadAndParseMortgageByPage_DYW(pageTable, mortgage);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析股权出质
        /// <summary>
        /// 解析股权出质
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseEquityQualityInfo(HtmlNode rootNode)
        {
            var request = this.CreateRequest();
            var table = rootNode.SelectSingleNode("//table[@class='table-result']");
            if (table != null)
            {
                this.LoadAndParseEquityQualityInfoByPage(table);

                var form = rootNode.SelectSingleNode("//form");
                if (form == null) return;
                var pagesDiv = form.SelectSingleNode("div[@class='pages']");
                if (pagesDiv == null) return;
                var input = pagesDiv.ChildNodes.FirstOrDefault(node => node.Name == "input" && node.Attributes.Contains("id") && node.Attributes["id"].Value == "pagescount");
                if (input == null) return;
                if (input != null)
                {
                    request.AddOrUpdateRequestParameter("equity_qualitityInfobypage_url", form.Attributes["action"].Value);
                    var pages = int.Parse(input.Attributes["value"].Value);

                    for (int i = 2; i <= pages; i++)
                    {
                        request.AddOrUpdateRequestParameter("pageNos", i.ToString());
                        request.AddOrUpdateRequestParameter("pageNo", (i - 1).ToString());
                        var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("equity_qualitityInfobypage"));

                        if (responseList != null && responseList.Any())
                        {
                            HtmlDocument document = new HtmlDocument();
                            document.LoadHtml(responseList.First().Data);
                            var rd = document.DocumentNode;
                            var pageTable = rd.SelectSingleNode("//table[@class='table-result']");
                            this.LoadAndParseEquityQualityInfoByPage(pageTable);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析股权出质--分页
        /// <summary>
        /// 解析股权出质--分页
        /// </summary>
        /// <param name="table"></param>
        void LoadAndParseEquityQualityInfoByPage(HtmlNode table)
        {
            var request = this.CreateRequest();
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Any() && tds.Count > 10)
                        {
                            EquityQuality equityQuality = new EquityQuality();
                            equityQuality.seq_no = _enterpriseInfo.equity_qualities.Count + 1;
                            equityQuality.number = tds[1].InnerText;
                            equityQuality.pledgor = tds[2].InnerText;
                            equityQuality.pledgor_identify_no = tds[3].InnerText;
                            equityQuality.pledgor_amount = tds[4].InnerText;
                            equityQuality.pawnee = tds[5].InnerText;
                            equityQuality.pawnee_identify_no = tds[6].InnerText;
                            equityQuality.date = tds[7].InnerText;
                            equityQuality.status = tds[8].InnerText;
                            equityQuality.public_date = tds[9].InnerText;
                            var a = tds.Last().SelectSingleNode("./a");
                            if (a != null)
                            {
                                var onclick = a.Attributes.Contains("onclick") ? a.Attributes["onclick"].Value : string.Empty;
                                if (!string.IsNullOrWhiteSpace(onclick))
                                {
                                    var arr = onclick.Split('\'');
                                    if (arr != null && arr.Length == 3)
                                    {
                                        request.AddOrUpdateRequestParameter("equity_qualitityInfo_detail_url", arr[1]);
                                        request.AddOrUpdateRequestParameter("time", this.GetTimeLikeJS().ToString());
                                        var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("equity_qualitityInfo_detail"));
                                        if (responseList != null && responseList.Any())
                                        {
                                            this.LoadAndParseEquityQualityDetailInfo(responseList.First().Data, equityQuality);
                                        }
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

        #region 解析股权出质详情信息
        /// <summary>
        /// 解析股权出质详情信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="equityQuality"></param>
        void LoadAndParseEquityQualityDetailInfo(string responseData, EquityQuality equityQuality)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            var table = rootNode.SelectSingleNode("//table[@class='table-result']");
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any() && trs.Count > 1)
                {
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Any() && tds.Count > 2)
                        {
                            ChangeItem item = new ChangeItem();
                            item.seq_no = equityQuality.change_items.Count + 1;
                            item.change_date = tds[1].InnerText;
                            item.change_content = tds[2].InnerText;
                            equityQuality.change_items.Add(item);
                        }

                    }
                }
            }
        }
        #endregion

        #region 解析抽查检查
        /// <summary>
        /// 解析抽查检查
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseCheckupInfo(HtmlNode rootNode)
        {
            var request = this.CreateRequest();
            var table = rootNode.SelectSingleNode("//table[@class='table-result']");
            if (table != null)
            {

                this.LoadAndParseCheckupInfoByPage(table);

                var form = rootNode.SelectSingleNode("//form");
                if (form == null) return;
                var pagesDiv = form.SelectSingleNode("div[@class='pages']");
                if (pagesDiv == null) return;
                var input = pagesDiv.ChildNodes.FirstOrDefault(node => node.Name == "input" && node.Attributes.Contains("id") && node.Attributes["id"].Value == "pagescount");
                if (input == null) return;
                var pages = int.Parse(input.Attributes["value"].Value);
                request.AddOrUpdateRequestParameter("stock_changeInfobypage_url", form.Attributes["action"].Value);

                for (int i = 2; i <= pages; i++)
                {
                    //request.AddOrUpdateRequestParameter("pageNos", i.ToString());
                    request.AddOrUpdateRequestParameter("pageNo", i.ToString());
                    var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("stock_changeInfobypage"));
                    if (responseList != null && responseList.Any())
                    {
                        HtmlDocument document = new HtmlDocument();
                        document.LoadHtml(responseList.First().Data);
                        var rd = document.DocumentNode;
                        var pageTable = rd.SelectSingleNode("//table[@class='table-result']");
                        this.LoadAndParseCheckupInfoByPage(pageTable);
                    }
                }
            }
        }
        #endregion

        #region 解析抽查检查--分页
        /// <summary>
        /// 解析抽查检查--分页
        /// </summary>
        /// <param name="table"></param>
        void LoadAndParseCheckupInfoByPage(HtmlNode table)
        {
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Any() && tds.Count == 5)
                        {
                            CheckupInfo checkupInfo = new CheckupInfo();
                            CheckupInfo item = new CheckupInfo();
                            item.name = _enterpriseInfo.name;
                            item.province = _enterpriseInfo.province;
                            item.reg_no = _enterpriseInfo.reg_no;
                            item.department = tds[1].InnerText;
                            item.type = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", ""); ;
                            item.date = tds[3].InnerText;
                            item.result = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");

                            _checkups.Add(item);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析经营异常
        /// <summary>
        /// 解析经营异常
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseAbnormalInfo(HtmlNode rootNode)
        {
            var request = this.CreateRequest();
            var table = rootNode.SelectSingleNode("//table[@class='table-result']");
            if (table != null)
            {
                this.LoadAndParseAbnormalInfoByPage(table);
                //var input = rootNode.SelectSingleNode("//input[@id='pagescount']");
                //if (input != null)
                //{
                //    var pages = int.Parse(input.Attributes["value"].Value);
                //    request.AddOrUpdateRequestParameter("pageNos", pages.ToString());
                //    for (int i = 2; i <= pages; i++)
                //    {
                //        request.AddOrUpdateRequestParameter("pageNo", i.ToString());
                //        var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("abnormalInfobypage"));
                //        if (responseList != null && responseList.Any())
                //        {
                //            HtmlDocument document = new HtmlDocument();
                //            document.LoadHtml(responseList.First().Data);
                //            var rd = document.DocumentNode;
                //            var pageTable = rd.SelectSingleNode("//table[@class='table-result']");
                //            this.LoadAndParseAbnormalInfoByPage(pageTable);
                //        }
                //    }
                //}
            }
        }
        #endregion

        #region 解析经营异常--分页
        /// <summary>
        /// 解析经营异常--分页
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseAbnormalInfoByPage(HtmlNode table)
        {
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Any() && tds.Count == 7)
                        {
                            AbnormalInfo item = new AbnormalInfo();
                            item.name = _enterpriseInfo.name;
                            item.reg_no = _enterpriseInfo.reg_no;
                            item.province = _enterpriseInfo.province;
                            item.in_reason = tds[1].InnerText;
                            item.in_date = tds[2].InnerText;
                            item.department = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            item.out_reason = tds[4].InnerText;
                            item.out_date = tds[5].InnerText;
                            _abnormals.Add(item);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析股东及出资信息
        /// <summary>
        /// 解析股东及出资信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseFinancialContributionInfo(HtmlNode rootNode)
        {
            var table = rootNode.SelectSingleNode("//table[@class='table-result']");
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Count > 2)
                {
                    trs.Remove(0);
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Any() && tds.Count == 5)
                        {
                            FinancialContribution financialContribution = new FinancialContribution();
                            financialContribution.seq_no = _enterpriseInfo.financial_contributions.Count + 1;
                            financialContribution.investor_name = tds[0].InnerText.Replace("&nbsp;", "");
                            financialContribution.total_should_capi = tds[1].InnerText.Replace("&nbsp;", "");
                            financialContribution.total_real_capi = tds[2].InnerText.Replace("&nbsp;", "");
                            var should_table = tds[3].SelectSingleNode("./table");
                            if (should_table != null)
                            {
                                var should_tr = should_table.SelectSingleNode("./tr");
                                if (should_tr != null)
                                {
                                    var should_tds = should_tr.SelectNodes("./td");
                                    if (should_tds != null && should_tds.Any() && should_tds.Count == 4)
                                    {
                                        FinancialContribution.ShouldCapiItem sci = new FinancialContribution.ShouldCapiItem();
                                        sci.should_invest_type = should_tds[0].InnerText.Replace("&nbsp;", "");
                                        sci.should_capi = should_tds[1].InnerText.Replace("&nbsp;", "");
                                        sci.should_invest_date = should_tds[2].InnerText.Replace("&nbsp;", "");
                                        sci.public_date = should_tds[3].InnerText.Replace("&nbsp;", "");
                                        financialContribution.should_capi_items.Add(sci);
                                    }
                                }
                            }
                            var real_table = tds[3].SelectSingleNode("./table");
                            if (real_table != null)
                            {
                                var real_tr = real_table.SelectSingleNode("./tr");
                                if (real_tr != null)
                                {
                                    var real_tds = real_tr.SelectNodes("./td");
                                    if (real_tds != null && real_tds.Any() && real_tds.Count == 4)
                                    {
                                        FinancialContribution.RealCapiItem rci = new FinancialContribution.RealCapiItem();
                                        rci.real_invest_type = real_tds[0].InnerText.Replace("&nbsp;", "");
                                        rci.real_capi = real_tds[1].InnerText.Replace("&nbsp;", "");
                                        rci.real_invest_date = real_tds[2].InnerText.Replace("&nbsp;", "");
                                        rci.public_date = real_tds[3].InnerText.Replace("&nbsp;", "");
                                        financialContribution.real_capi_items.Add(rci);
                                    }
                                }
                            }
                            _enterpriseInfo.financial_contributions.Add(financialContribution);
                        }

                    }
                }
            }
        }
        #endregion

        #region 解析股权变更信息
        /// <summary>
        /// 解析股权变更信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseStockChangeInfo(HtmlNode rootNode)
        {
            var request = this.CreateRequest();
            var table = rootNode.SelectSingleNode("//table[@class='table-result']");
            if (table != null)
            {

                this.LoadAndParseStockChangeInfoByPage(table);

                var form = rootNode.SelectSingleNode("//form");
                if (form == null) return;
                var pagesDiv = form.SelectSingleNode("div[@class='pages']");
                if (pagesDiv == null) return;
                var input = pagesDiv.ChildNodes.FirstOrDefault(node => node.Name == "input" && node.Attributes.Contains("id") && node.Attributes["id"].Value == "pagescount");
                if (input == null) return;
                var pages = int.Parse(input.Attributes["value"].Value);
                request.AddOrUpdateRequestParameter("stock_changeInfobypage_url", form.Attributes["action"].Value);

                for (int i = 2; i <= pages; i++)
                {
                    //request.AddOrUpdateRequestParameter("pageNos", i.ToString());
                    request.AddOrUpdateRequestParameter("pageNo", i.ToString());
                    var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("stock_changeInfobypage"));
                    if (responseList != null && responseList.Any())
                    {
                        HtmlDocument document = new HtmlDocument();
                        document.LoadHtml(responseList.First().Data);
                        var rd = document.DocumentNode;
                        var pageTable = rd.SelectSingleNode("//table[@class='table-result']");
                        this.LoadAndParseStockChangeInfoByPage(pageTable);
                    }
                }
            }
        }
        #endregion

        #region 解析股权变更信息--分页
        /// <summary>
        /// 解析股权变更信息--分页
        /// </summary>
        /// <param name="table"></param>
        void LoadAndParseStockChangeInfoByPage(HtmlNode table)
        {
            var trs = table.SelectNodes("./tr");
            if (trs != null && trs.Any() && trs.Count > 1)
            {
                foreach (var tr in trs)
                {
                    var tds = tr.SelectNodes("./td");
                    if (tds != null && tds.Any() && tds.Count == 6)
                    {
                        StockChangeItem sci = new StockChangeItem();
                        sci.seq_no = _enterpriseInfo.stock_changes.Count + 1;
                        sci.name = tds[1].InnerText;
                        sci.before_percent = tds[2].InnerText;
                        sci.after_percent = tds[3].InnerText;
                        sci.change_date = tds[4].InnerText;
                        sci.public_date = tds[5].InnerText;
                        _enterpriseInfo.stock_changes.Add(sci);
                    }
                }
            }
        }
        #endregion

        #region 解析行政许可信息
        /// <summary>
        /// 解析行政许可信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseLicenceInfo(HtmlNode rootNode)
        {
            var request = this.CreateRequest();
            var table = rootNode.SelectSingleNode("//table[@class='table-result']");
            if (table != null)
            {

                this.LoadAndParseLicenceInfoByPage(table);

                var form = rootNode.SelectSingleNode("//form");
                if (form == null) return;
                var pagesDiv = form.SelectSingleNode("div[@class='pages']");
                if (pagesDiv == null) return;
                var input = pagesDiv.ChildNodes.FirstOrDefault(node => node.Name == "input" && node.Attributes.Contains("id") && node.Attributes["id"].Value == "pagescount");
                if (input == null) return;
                var pages = int.Parse(input.Attributes["value"].Value);
                request.AddOrUpdateRequestParameter("licenceInfobypage_url", form.Attributes["action"].Value);

                for (int i = 2; i <= pages; i++)
                {
                    //request.AddOrUpdateRequestParameter("pageNos", i.ToString());
                    request.AddOrUpdateRequestParameter("pageNo", i.ToString());
                    var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("licenceInfobypage"));
                    if (responseList != null && responseList.Any())
                    {
                        HtmlDocument document = new HtmlDocument();
                        document.LoadHtml(responseList.First().Data);
                        var rd = document.DocumentNode;
                        var pageTable = rd.SelectSingleNode("//table[@class='table-result']");
                        this.LoadAndParseLicenceInfoByPage(pageTable);
                    }
                }
            }
        }
        #endregion

        #region 解析行政许可信息--分页
        /// <summary>
        /// 解析行政许可信息--分页
        /// </summary>
        /// <param name="table"></param>
        void LoadAndParseLicenceInfoByPage(HtmlNode table)
        {
            var trs = table.SelectNodes("./tr");
            if (trs != null && trs.Any() && trs.Count > 1)
            {
                foreach (var tr in trs)
                {
                    var tds = tr.SelectNodes("./td");
                    if (tds != null && tds.Any() && tds.Count == 9)
                    {
                        LicenseInfo licenseInfo = new LicenseInfo();
                        licenseInfo.seq_no = _enterpriseInfo.licenses.Count + 1;
                        licenseInfo.number = tds[1].InnerText.Replace("&nbsp;", "");
                        licenseInfo.name = tds[2].InnerText.Replace("&nbsp;", "");
                        licenseInfo.start_date = tds[3].InnerText.Replace("&nbsp;", "");
                        licenseInfo.end_date = tds[4].InnerText.Replace("&nbsp;", "");
                        licenseInfo.department = tds[5].InnerText.Replace("&nbsp;", "");
                        licenseInfo.content = tds[6].InnerText.Replace("&nbsp;", "");
                        licenseInfo.status = tds[7].InnerText.Replace("&nbsp;", "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        _enterpriseInfo.licenses.Add(licenseInfo);
                    }
                }
            }
        }
        #endregion

        #region 解析行政许可信息
        /// <summary>
        /// 解析行政许可信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseLicenceInfo2(HtmlNode rootNode)
        {
            var request = this.CreateRequest();
            var table = rootNode.SelectSingleNode("//table[@class='table-result']");
            if (table != null)
            {

                this.LoadAndParseLicenceInfoByPage2(table);

                var form = rootNode.SelectSingleNode("//form");
                if (form == null) return;
                var pagesDiv = form.SelectSingleNode("div[@class='pages']");
                if (pagesDiv == null) return;
                var input = pagesDiv.ChildNodes.FirstOrDefault(node => node.Name == "input" && node.Attributes.Contains("id") && node.Attributes["id"].Value == "pagescount");
                if (input == null) return;
                var pages = int.Parse(input.Attributes["value"].Value);
                request.AddOrUpdateRequestParameter("licenceInfobypage_url", form.Attributes["action"].Value);

                for (int i = 2; i <= pages; i++)
                {
                    //request.AddOrUpdateRequestParameter("pageNos", i.ToString());
                    request.AddOrUpdateRequestParameter("pageNo", i.ToString());
                    var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("licenceInfobypage"));
                    if (responseList != null && responseList.Any())
                    {
                        HtmlDocument document = new HtmlDocument();
                        document.LoadHtml(responseList.First().Data);
                        var rd = document.DocumentNode;
                        var pageTable = rd.SelectSingleNode("//table[@class='table-result']");
                        this.LoadAndParseLicenceInfoByPage2(pageTable);
                    }
                }
            }
        }
        #endregion

        #region 解析行政许可信息--分页
        /// <summary>
        /// 解析行政许可信息--分页
        /// </summary>
        /// <param name="table"></param>
        void LoadAndParseLicenceInfoByPage2(HtmlNode table)
        {
            if (table == null) return;
            var trs = table.SelectNodes("./tr");
            if (trs != null && trs.Any() && trs.Count > 1)
            {
                foreach (var tr in trs)
                {
                    var tds = tr.SelectNodes("./td");
                    if (tds != null && tds.Any() && tds.Count == 7)
                    {
                        LicenseInfo licenseInfo = new LicenseInfo();
                        licenseInfo.seq_no = _enterpriseInfo.licenses.Count + 1;
                        licenseInfo.number = tds[1].InnerText.Replace("&nbsp;", "");
                        licenseInfo.name = tds[2].InnerText.Replace("&nbsp;", "");
                        licenseInfo.start_date = tds[3].InnerText.Replace("&nbsp;", "");
                        licenseInfo.end_date = tds[4].InnerText.Replace("&nbsp;", "");
                        licenseInfo.department = tds[5].InnerText.Replace("&nbsp;", "");
                        licenseInfo.content = tds[6].InnerText.Replace("&nbsp;", "");
                        Utility.ClearNullValue<LicenseInfo>(licenseInfo);
                        _enterpriseInfo.licenses.Add(licenseInfo);
                    }
                }
            }
        }
        #endregion

        #region 解析知识产权出质登记信息
        /// <summary>
        /// 解析知识产权出质登记信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseKnowledgePropertyInfo(HtmlNode rootNode)
        {
            var request = this.CreateRequest();
            var table = rootNode.SelectSingleNode("//table[@class='table-result']");
            if (table != null)
            {

                this.LoadAndParseKnowledgePropertyInfoByPage(table);

                var form = rootNode.SelectSingleNode("//form");
                if (form == null) return;
                var pagesDiv = form.SelectSingleNode("div[@class='pages']");
                if (pagesDiv == null) return;
                var input = pagesDiv.ChildNodes.FirstOrDefault(node => node.Name == "input" && node.Attributes.Contains("id") && node.Attributes["id"].Value == "pagescount");
                if (input == null) return;
                var pages = int.Parse(input.Attributes["value"].Value);
                request.AddOrUpdateRequestParameter("knowledge_propertyInfobypage_url", form.Attributes["action"].Value);

                for (int i = 2; i <= pages; i++)
                {
                    //request.AddOrUpdateRequestParameter("pageNos", i.ToString());
                    request.AddOrUpdateRequestParameter("pageNo", i.ToString());
                    var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("knowledge_propertyInfobypage"));
                    if (responseList != null && responseList.Any())
                    {
                        HtmlDocument document = new HtmlDocument();
                        document.LoadHtml(responseList.First().Data);
                        var rd = document.DocumentNode;
                        var pageTable = rd.SelectSingleNode("//table[@class='table-result']");
                        this.LoadAndParseKnowledgePropertyInfoByPage(pageTable);
                    }
                }
            }
        }
        #endregion

        #region 解析知识产权出质登记信息--分页
        /// <summary>
        /// 解析知识产权出质登记信息--分页
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseKnowledgePropertyInfoByPage(HtmlNode table)
        {
            var trs = table.SelectNodes("./tr");
            if (trs != null && trs.Any() && trs.Count > 1)
            {
                foreach (var tr in trs)
                {
                    var tds = tr.SelectNodes("./td");
                    if (tds != null && tds.Any() && tds.Count == 10)
                    {
                        KnowledgeProperty knowledgeProperty = new KnowledgeProperty();
                        knowledgeProperty.seq_no = _enterpriseInfo.knowledge_properties.Count + 1;
                        knowledgeProperty.number = tds[1].InnerText.Replace("&nbsp;", "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        knowledgeProperty.name = tds[2].InnerText.Replace("&nbsp;", "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        knowledgeProperty.type = tds[3].InnerText.Replace("&nbsp;", "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        knowledgeProperty.pledgor = tds[4].InnerText.Replace("&nbsp;", "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        knowledgeProperty.pawnee = tds[5].InnerText.Replace("&nbsp;", "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        knowledgeProperty.period = tds[6].InnerText.Replace("&nbsp;", "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        knowledgeProperty.status = tds[7].InnerText.Replace("&nbsp;", "").Replace("\r", "").Replace("\n", "").Replace("\t", "");
                        knowledgeProperty.public_date = tds[8].InnerText.Replace("&nbsp;", "").Replace("\r", "").Replace("\n", "").Replace("\t", "");
                        _enterpriseInfo.knowledge_properties.Add(knowledgeProperty);
                    }
                }
            }
        }
        #endregion

        #region 解析行政处罚信息
        /// <summary>
        /// 解析行政处罚信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseAdministrativePunishment(HtmlNode rootNode)
        {
            var request = this.CreateRequest();
            var table = rootNode.SelectSingleNode("//table[@class='table-result']");
            if (table != null)
            {
                this.LoadAndParseAdministrativePunishmentByPage(table);
                var input = rootNode.SelectSingleNode("//input[@id='pagescount']");
                if (input != null)
                {
                    var pages = int.Parse(input.Attributes["value"].Value);

                    for (int i = 2; i <= pages; i++)
                    {
                        request.AddOrUpdateRequestParameter("pageNos", i.ToString());
                        request.AddOrUpdateRequestParameter("pageNo", (i - 1).ToString());
                        var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("administrative_punishmentInfobypage"));
                        if (responseList != null && responseList.Any())
                        {
                            HtmlDocument document = new HtmlDocument();
                            document.LoadHtml(responseList.First().Data);
                            var rd = document.DocumentNode;
                            var pageTable = rd.SelectSingleNode("//table[@class='table-result']");
                            this.LoadAndParseAdministrativePunishmentByPage(pageTable);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析行政处罚信息--分页
        /// <summary>
        /// 解析行政处罚信息--分页
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseAdministrativePunishmentByPage(HtmlNode table)
        {
            var request = this.CreateRequest();
            var trs = table.SelectNodes("./tr");
            if (trs != null && trs.Any() && trs.Count > 1)
            {
                foreach (var tr in trs)
                {
                    var tds = tr.SelectNodes("./td");
                    if (tds != null && tds.Any() && tds.Count == 8)
                    {
                        AdministrativePunishment ap = new AdministrativePunishment();
                        ap.seq_no = _enterpriseInfo.administrative_punishments.Count + 1;
                        ap.number = tds[1].InnerText.Replace("&nbsp;", "");
                        ap.illegal_type = tds[2].InnerText.Replace("&nbsp;", "");
                        ap.content = tds[3].InnerText.Replace("&nbsp;", "");
                        ap.department = tds[4].InnerText.Replace("&nbsp;", "");
                        ap.date = tds[5].InnerText.Replace("&nbsp;", "");
                        ap.name = _enterpriseInfo.name;
                        ap.oper_name = _enterpriseInfo.oper_name;
                        ap.reg_no = string.IsNullOrWhiteSpace(_enterpriseInfo.reg_no) ? _enterpriseInfo.credit_no : _enterpriseInfo.reg_no;
                        ap.public_date = tds[6].InnerText.Replace("&nbsp;", "");
                        ap.remark = tds[7].InnerText.Replace("&nbsp;", "");
                        _enterpriseInfo.administrative_punishments.Add(ap);
                    }
                }
            }
        }
        #endregion

        #region 解析行政处罚详情信息
        /// <summary>
        /// 解析行政处罚详情信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="ap"></param>
        void LoadAndParseAdministrativePunishmentDetail(string responseData, AdministrativePunishment ap)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            var table = rootNode.SelectSingleNode("//div[@class='qyqx-detail']/table");
            if (table != null)
            {
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
                                var title = td.SelectSingleNode("./strong").InnerText;
                                var val = td.InnerText.Replace(title, "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", ""); ;
                                switch (title)
                                {
                                    case "行政处罚决定书文号：":
                                        ap.number = val;
                                        break;
                                    case "作出行政处罚机关名称：":
                                        ap.department = val;
                                        break;
                                    case "名称：":
                                        ap.name = val;
                                        break;
                                    case "统一社会信用代码/注册号：":
                                        ap.reg_no = val;
                                        break;
                                    case "法定代表人（负责人）姓名：":
                                        ap.oper_name = val;
                                        break;
                                    case "作出行政处罚决定日期：":
                                        ap.date = val;
                                        break;
                                    case "违法行为类型：":
                                        ap.illegal_type = val;
                                        break;
                                    case "行政处罚内容：":
                                        ap.content = val;
                                        break;
                                    default:
                                        break;
                                }
                            }
                        }
                    }
                }
            }
            var file = rootNode.SelectSingleNode("//div[@id='xzcf_file']");
            if (file != null)
            {
                ap.description = file.InnerHtml;
            }
        }
        #endregion

        #region 解析行政处罚信息
        /// <summary>
        /// 解析行政处罚信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseAdministrativePunishment2(HtmlNode rootNode)
        {
            var request = this.CreateRequest();
            var table = rootNode.SelectSingleNode("//table[@class='table-result']");
            if (table != null)
            {
                this.LoadAndParseAdministrativePunishmentByPage2(table);
                var input = rootNode.SelectSingleNode("//input[@id='pagescount']");
                if (input != null)
                {
                    var pages = int.Parse(input.Attributes["value"].Value);

                    for (int i = 2; i <= pages; i++)
                    {
                        request.AddOrUpdateRequestParameter("pageNos", i.ToString());
                        request.AddOrUpdateRequestParameter("pageNo", (i - 1).ToString());
                        var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("administrative_punishmentInfobypage2"));
                        if (responseList != null && responseList.Any())
                        {
                            HtmlDocument document = new HtmlDocument();
                            document.LoadHtml(responseList.First().Data);
                            var rd = document.DocumentNode;
                            var pageTable = rd.SelectSingleNode("//table[@class='table-result']");
                            this.LoadAndParseAdministrativePunishmentByPage2(pageTable);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析行政处罚信息--分页
        /// <summary>
        /// 解析行政处罚信息--分页
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseAdministrativePunishmentByPage2(HtmlNode table)
        {
            var request = this.CreateRequest();
            var trs = table.SelectNodes("./tr");
            if (trs != null && trs.Any() && trs.Count > 1)
            {
                foreach (var tr in trs)
                {
                    var tds = tr.SelectNodes("./td");
                    if (tds != null && tds.Any() && tds.Count == 8)
                    {
                        AdministrativePunishment ap = new AdministrativePunishment();
                        ap.seq_no = _enterpriseInfo.administrative_punishments.Count + 1;
                        ap.number = tds[1].InnerText.Replace("&nbsp;", "");
                        ap.illegal_type = tds[2].InnerText.Replace("&nbsp;", "");
                        ap.content = tds[3].InnerText.Replace("&nbsp;", "");
                        ap.department = tds[4].InnerText.Replace("&nbsp;", "");
                        ap.date = tds[5].InnerText.Replace("&nbsp;", "");
                        var a = tds.Last().SelectSingleNode("./a");
                        if (a != null)
                        {
                            var href = a.Attributes.Contains("href") ? a.Attributes["href"].Value : string.Empty;
                            if (!string.IsNullOrWhiteSpace(href))
                            {
                                request.AddOrUpdateRequestParameter("administrative_punishmentInfo_detail_url2", href);
                                var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("administrative_punishmentInfo_detail2"));
                                if (responseList != null && responseList.Any())
                                {
                                    this.LoadAndParseAdministrativePunishmentDetail2(responseList.First().Data, ap);
                                }
                            }
                        }
                        _enterpriseInfo.administrative_punishments.Add(ap);
                    }
                }
            }
        }
        #endregion

        #region 解析行政处罚详情信息
        /// <summary>
        /// 解析行政处罚详情信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="ap"></param>
        void LoadAndParseAdministrativePunishmentDetail2(string responseData, AdministrativePunishment ap)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            var table = rootNode.SelectSingleNode("//div[@class='qyqx-detail']/table");
            if (table != null)
            {
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
                                var title = td.SelectSingleNode("./strong").InnerText;
                                var val = td.InnerText.Replace(title, "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                switch (title)
                                {
                                    case "行政处罚决定书文号：":
                                        ap.number = val;
                                        break;
                                    case "作出行政处罚机关名称：":
                                        ap.department = val;
                                        break;
                                    case "名称：":
                                        ap.name = val;
                                        break;
                                    case "统一社会信用代码/注册号：":
                                        ap.reg_no = val;
                                        break;
                                    case "法定代表人（负责人）姓名：":
                                        ap.oper_name = val;
                                        break;
                                    case "作出行政处罚决定日期：":
                                        ap.date = val;
                                        break;
                                    case "违法行为类型：":
                                        ap.illegal_type = val;
                                        break;
                                    case "行政处罚内容：":
                                        ap.content = val;
                                        break;
                                    default:
                                        break;
                                }
                            }
                        }
                    }
                }
            }
            var file = rootNode.SelectSingleNode("//div[@id='xzcf_file']");
            if (file != null)
            {
                ap.description = file.InnerHtml;
            }
        }
        #endregion

        #region 解析年报信息
        /// <summary>
        /// 解析年报信息
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseReportInfo(HtmlNode rootNode)
        {
            var table = rootNode.SelectSingleNode("//table[@class='table-result']");
            if (table != null)
            {

                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any() && trs.Count > 1)
                {
                    try
                    {
                        Parallel.ForEach(trs, new ParallelOptions { MaxDegreeOfParallelism = _parallelCount }, tr => this.LoadAndParseReport_Parallel(tr));
                    }
                    catch (AggregateException ex)
                    {
                        _enterpriseInfo.reports.Clear();
                    }
                }
            }
        }

        #endregion

        #region 解析年报信息--并行
        /// <summary>
        /// 解析年报信息--并行
        /// </summary>
        /// <param name="tr"></param>
        void LoadAndParseReport_Parallel(HtmlNode tr)
        {
            var request = this.CreateRequest();
            var tds = tr.SelectNodes("./td");
            if (tds != null && tds.Any() && tds.Count == 4)
            {
                Report report = new Report();
                report.report_name = tds[1].InnerText;
                report.report_year = tds[1].InnerText.Substring(0, 4);
                report.report_date = tds[2].InnerText;
                var a = tds.Last().SelectSingleNode("./a");
                if (a != null)
                {
                    var href = a.Attributes.Contains("href") ? a.Attributes["href"].Value : string.Empty;
                    if (!string.IsNullOrWhiteSpace(href))
                    {
                        try
                        {
                            request.AddOrUpdateRequestParameter("reportInfo_detail_url", href);
                            var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("reportInfo_detail"));
                            if (responseList != null && responseList.Any())
                            {
                                this.LoadAndParseReportDetailInfo(responseList.First().Data, report);
                            }
                        }
                        catch { }
                    }
                }
                _enterpriseInfo.reports.Add(report);
            }
        }
        #endregion

        #region 解析企业年报详情信息
        /// <summary>
        /// 解析企业年报详情信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>

        void LoadAndParseReportDetailInfo(string responseData, Report report)
        {
            HtmlDocument document = new HtmlDocument();
            HtmlNode.ElementsFlags.Remove("iframe");
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            this.LoadAndParseBasicInfo_Report(rootNode, report);
            this.LoadAndParseConditionOfAssets(rootNode, report);
            this.LoadAndParseSheBao(rootNode, report);
            this.LoadAndParseWebsite_Report(rootNode, report);
            this.LoadAndParsePartnerInfo_Report(rootNode, report);
            this.LoadAndParseInvestInfo_Report(rootNode, report);
            this.LoadAndParseExternalGuaranteeInfo_Report(rootNode, report);
            this.LoadAndParseUpdateRecordInfo_Report(rootNode, report);
        }
        #endregion

        #region 解析年报基本信息
        /// <summary>
        /// 解析年报基本信息
        /// </summary>
        /// <param name="rootNode"></param>
        /// <param name="report"></param>
        void LoadAndParseBasicInfo_Report(HtmlNode rootNode, Report report)
        {
            var table = rootNode.SelectSingleNode("//table[@class='detail-list1 qy-list']");
            if (table != null)
            {
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
                                if (string.IsNullOrWhiteSpace(td.InnerText)) continue;
                                var title = td.SelectSingleNode("./strong").InnerText;
                                var val = td.InnerText.Replace(title, "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                switch (title)
                                {
                                    case "注册号：":
                                    case "统一社会信用代码：":
                                    case "注册号/统一社会信用代码：":
                                    case "统一社会信用代码/注册号：":
                                        if (val.Length == 18)
                                        {
                                            report.credit_no = val;
                                        }
                                        else
                                        {
                                            report.reg_no = val;
                                        }
                                        break;
                                    case "名称：":
                                    case "企业名称：":
                                    case "合作社名称：":
                                        report.name = val.Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                                        break;
                                    case "联系电话：":
                                    case "企业联系电话：":
                                    case "经营者联系电话：":
                                        report.telephone = val.Trim();
                                        break;
                                    case "企业通信地址：":
                                        report.address = val.Trim();
                                        break;
                                    case "邮政编码：":
                                        report.zip_code = val.Trim();
                                        break;
                                    case "电子邮箱：":
                                    case "企业电子邮箱：":
                                        report.email = val.Trim();
                                        break;
                                    case "企业是否有投资信息或购买其他公司股权：":
                                    case "企业是否有对外投资设立企业信息：":
                                        report.if_invest = val.Trim();
                                        break;
                                    case "是否有网站或网店：":
                                        report.if_website = val.Trim();
                                        break;
                                    case "企业经营状态：":
                                    case "经营状态：":
                                        report.status = val.Trim();
                                        break;
                                    case "从业人数：":
                                    case "成员人数：":
                                        report.collegues_num = val.Trim();
                                        break;
                                    case "有限责任公司本年度是否发生股东股权转让：":
                                        report.if_equity = val.Trim();
                                        break;
                                    case "经营者姓名：":
                                        report.oper_name = val.Trim();
                                        break;
                                    case "资金数额：":
                                        report.total_equity = val.Trim();
                                        break;
                                    case "是否有对外提供担保信息：":
                                        report.if_external_guarantee = val.Trim();
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

        #region 解析企业资产状况信息
        /// <summary>
        /// 解析企业资产状况信息
        /// </summary>
        /// <param name="rootNode"></param>
        /// <param name="report"></param>
        void LoadAndParseConditionOfAssets(HtmlNode rootNode, Report report)
        {
            var table = rootNode.SelectSingleNode("//table[@class='table-result']");
            if (table != null)
            {

                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    foreach (var tr in trs)
                    {
                        var thList = tr.SelectNodes("./th");
                        var tdList = tr.SelectNodes("./td");
                        if (thList != null && tdList != null && thList.Count == tdList.Count)
                        {
                            for (int i = 0; i < thList.Count; i++)
                            {
                                var title = thList[i].InnerText.Trim();
                                var val = tdList[i].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace("&nbsp;", "").Trim();
                                switch (title)
                                {
                                    case "资产总额":
                                        report.total_equity = val;
                                        break;
                                    case "负债总额":
                                    case "金融贷款":
                                        report.debit_amount = val;
                                        break;
                                    case "营业额或营业收入":
                                    case "销售总额":
                                    case "营业总收入":
                                    case "销售(营业)收入":
                                        report.sale_income = val;
                                        break;
                                    case "其中：主营业务收入":
                                    case "营业总收入中主营业务收入":
                                        report.serv_fare_income = val;
                                        break;
                                    case "利润总额":
                                    case "盈余总额":
                                        report.profit_total = val;
                                        break;
                                    case "净利润":
                                        report.net_amount = val;
                                        break;
                                    case "纳税总额":
                                    case "纳税总额：":
                                        report.tax_total = val;
                                        break;
                                    case "所有者权益合计":
                                    case "获得政府扶持资金、补助":
                                        report.profit_reta = val;
                                        break;
                                    default:
                                        break;
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                var h1List = rootNode.SelectNodes("//h1[@class='public-title2 qy-title']");
                if (h1List != null && h1List.Any())
                {
                    foreach (var h1 in h1List)
                    {
                        var t = h1.InnerText;
                        if (t == "生产经营情况信息")
                        {
                            table = h1.SelectSingleNode("./following-sibling::table[1]");
                            if (table != null)
                            {
                                var trs = table.SelectNodes("./tr");
                                foreach (var tr in trs)
                                {
                                    var tds = tr.SelectNodes("./td");
                                    if (tds != null && tds.Any())
                                    {
                                        foreach (var td in tds)
                                        {
                                            if (string.IsNullOrWhiteSpace(td.InnerText)) continue;
                                            var title = td.SelectSingleNode("./strong").InnerText;
                                            var val = td.InnerText.Replace(title, "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                                            switch (title)
                                            {
                                                case "销售(营业)收入：":
                                                    report.sale_income = val;
                                                    break;
                                                case "纳税总额：":
                                                    report.tax_total = val;
                                                    break;
                                                default:
                                                    break;
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
        }
        #endregion

        #region 解析社保信息
        /// <summary>
        /// 解析社保信息
        /// </summary>
        /// <param name="rootNode"></param>
        /// <param name="report"></param>
        void LoadAndParseSheBao(HtmlNode rootNode, Report report)
        {
            var tables = rootNode.SelectNodes("//table[@class='table-result']");
            if (tables != null && tables.Any())
            {
                foreach (var table in tables)
                {
                    var h1 = table.SelectSingleNode("./preceding-sibling::h1[1]");
                    if (h1 != null)
                    {
                        var title = h1.InnerText;
                        if (title.Contains("社保信息"))
                        {
                            this.LoadAndParseSheBaoContent(table, report);
                            break;
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析社保内容
        /// <summary>
        /// 解析社保内容
        /// </summary>
        /// <param name="table"></param>
        /// <param name="report"></param>
        void LoadAndParseSheBaoContent(HtmlNode table, Report report)
        {
            HtmlNodeCollection trList = table.ParentNode.SelectNodes("./table/tr");

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
                            case "参加工伤保险累计欠缴金额":
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

        #region 解析网站信息
        /// <summary>
        /// 解析网站信息
        /// </summary>
        /// <param name="rootNode"></param>
        /// <param name="report"></param>
        void LoadAndParseWebsite_Report(HtmlNode rootNode, Report report)
        {
            var request = this.CreateRequest();
            var iframe = rootNode.SelectSingleNode("//iframe[@id='wzFrame']");
            if (iframe != null)
            {
                var http = "http://gx.gsxt.gov.cn";
                var src = iframe.OuterHtml.Split('\'')[1];
                if (!string.IsNullOrWhiteSpace(src))
                {
                    var url = string.Format("{0}{1}", http, src);
                    var requestList = new List<RequestSetting>();

                    requestList.Add(new RequestSetting()
                    {
                        Name = "Website_Report",
                        Method = "get",
                        IsArray = "0",
                        Url = url
                    });
                    var responseList = request.GetResponseInfo(requestList);
                    if (responseList != null && responseList.Any())
                    {
                        HtmlDocument document = new HtmlDocument();
                        HtmlNode.ElementsFlags.Remove("form");
                        HtmlNode.ElementsFlags.Remove("input");
                        document.LoadHtml(responseList.First().Data);
                        var rd = document.DocumentNode;
                        var tables = rd.SelectNodes("//table[@class='detailsList']");
                        if (tables != null && tables.Any())
                        {
                            this.LoadAndParseWebsite_Report(tables, report);
                        }

                    }
                }
            }
        }
        #endregion

        #region 解析网站信息分页--年报
        /// <summary>
        /// 解析网站信息分页--年报
        /// </summary>
        /// <param name="tables"></param>
        /// <param name="report"></param>
        void LoadAndParseWebsite_Report(HtmlNodeCollection tables, Report report)
        {
            foreach (var table in tables)
            {
                var trs = table.SelectNodes("./tbody/tr");
                if (trs != null && trs.Any() && trs.Count == 3)
                {
                    WebsiteItem item = new WebsiteItem();
                    var first = trs.First();
                    var second = trs[1];
                    var last = trs.Last();

                    var first_td = first.SelectSingleNode("./td");
                    var second_td = second.SelectSingleNode("./td");
                    var last_td = last.SelectSingleNode("./td");

                    item.seq_no = report.websites.Count + 1;
                    item.web_name = first_td.InnerText;
                    item.web_type = second_td.InnerText.Replace("· 类型：", "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Trim();
                    item.web_url = last_td.InnerText.Replace("· 网址：", "");
                    report.websites.Add(item);
                }
            }
        }
        #endregion

        #region 解析股东及出资信息--年报
        /// <summary>
        /// 解析股东及出资信息--年报
        /// </summary>
        /// <param name="rootNode"></param>
        /// <param name="report"></param>
        void LoadAndParsePartnerInfo_Report(HtmlNode rootNode, Report report)
        {
            var request = this.CreateRequest();
            var iframe = rootNode.SelectSingleNode("//iframe[@id='gdczFrame']");
            if (iframe != null)
            {
                var http = "http://gx.gsxt.gov.cn";
                var src = iframe.OuterHtml.Split('\'')[1];
                if (!string.IsNullOrWhiteSpace(src))
                {
                    var url = string.Format("{0}{1}", http, src);
                    var requestList = new List<RequestSetting>();

                    requestList.Add(new RequestSetting()
                    {
                        Name = "Partner_Report",
                        Method = "get",
                        IsArray = "0",
                        Url = url
                    });
                    var responseList = request.GetResponseInfo(requestList);
                    if (responseList != null && responseList.Any())
                    {
                        HtmlDocument document = new HtmlDocument();
                        HtmlNode.ElementsFlags.Remove("form");
                        HtmlNode.ElementsFlags.Remove("input");
                        document.LoadHtml(responseList.First().Data);
                        var rd = document.DocumentNode;
                        var table = rd.SelectSingleNode("//table[@class='table-result']");
                        if (table != null)
                        {
                            this.LoadAndParsePartnerInfoByPage_Report(table, report);
                            var form = rootNode.SelectSingleNode("//form");
                            if (form == null) return;
                            var entidInput = rd.SelectSingleNode("//input[@name='entid']").Attributes["Value"].Value.Trim();
                            var cidInput = rd.SelectSingleNode("//input[@name='cid']").Attributes["Value"].Value.Trim();
                            request.AddOrUpdateRequestParameter("report_entid", entidInput);
                            request.AddOrUpdateRequestParameter("report_cid", cidInput);
                            var pagesDiv = rd.SelectSingleNode("//div[@class='pages']");
                            if (pagesDiv == null) return;
                            var input = pagesDiv.ChildNodes.FirstOrDefault(node => node.Name == "input" && node.Attributes.Contains("id") && node.Attributes["id"].Value == "pagescount");
                            if (input == null) return;

                            var pages = int.Parse(input.Attributes["value"].Value);
                            request.AddOrUpdateRequestParameter("reportInfo_detail_partner_url", src);

                            for (int i = 2; i <= pages; i++)
                            {
                                request.AddOrUpdateRequestParameter("pageNos", i.ToString());
                                request.AddOrUpdateRequestParameter("pageNo", (i - 1).ToString());
                                var resList = request.GetResponseInfo(_requestXml.GetRequestListByName("reportInfo_detail_partner"));
                                if (resList != null && resList.Any())
                                {
                                    HtmlDocument documentPage = new HtmlDocument();
                                    documentPage.LoadHtml(resList.First().Data);
                                    var rdPage = documentPage.DocumentNode;
                                    var pageTable = rdPage.SelectSingleNode("//table[@class='table-result']");
                                    this.LoadAndParsePartnerInfoByPage_Report(pageTable, report);
                                }
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析股东及出资信息分页--年报
        /// <summary>
        /// 解析股东及出资信息分页--年报
        /// </summary>
        /// <param name="table"></param>
        /// <param name="report"></param>
        void LoadAndParsePartnerInfoByPage_Report(HtmlNode table, Report report)
        {
            if (table == null) return;
            var trs = table.SelectNodes("./tr");
            if (trs != null && trs.Any())
            {
                foreach (var tr in trs)
                {
                    var tds = tr.SelectNodes("./td");
                    if (tds != null && tds.Any() && tds.Count == 8)
                    {
                        Partner partner = new Partner();
                        partner.seq_no = report.partners.Count + 1;
                        partner.stock_name = tds[1].InnerText;

                        ShouldCapiItem sci = new ShouldCapiItem();
                        sci.shoud_capi = tds[2].InnerText;
                        sci.should_capi_date = tds[3].InnerText;
                        sci.invest_type = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        partner.should_capi_items.Add(sci);

                        RealCapiItem rci = new RealCapiItem();
                        rci.real_capi = tds[5].InnerText;
                        rci.real_capi_date = tds[6].InnerText;
                        rci.invest_type = tds[7].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        partner.real_capi_items.Add(rci);

                        report.partners.Add(partner);
                    }
                }
            }
        }
        #endregion

        #region 解析对外投资信息
        /// <summary>
        /// 解析对外投资信息
        /// </summary>
        /// <param name="rootNode"></param>
        /// <param name="report"></param>
        void LoadAndParseInvestInfo_Report(HtmlNode rootNode, Report report)
        {
            var request = this.CreateRequest();
            var iframe = rootNode.SelectSingleNode("//iframe[@id='dwtzFrame']");
            if (iframe != null)
            {
                var http = "http://gx.gsxt.gov.cn";
                var src = iframe.OuterHtml.Split('\'')[1];
                if (src != null)
                {
                    var url = string.Format("{0}{1}", http, src);
                    var requestList = new List<RequestSetting>();

                    requestList.Add(new RequestSetting()
                    {
                        Name = "Invest_Report",
                        Method = "get",
                        IsArray = "0",
                        Url = url
                    });
                    var responseList = request.GetResponseInfo(requestList);
                    if (responseList != null && responseList.Any())
                    {
                        HtmlDocument document = new HtmlDocument();
                        HtmlNode.ElementsFlags.Remove("form");
                        HtmlNode.ElementsFlags.Remove("input");
                        document.LoadHtml(responseList.First().Data);
                        var rd = document.DocumentNode;
                        var tables = rd.SelectNodes("//table[@class='detailsList']");
                        if (tables != null && tables.Any())
                        {
                            this.LoadAndParseInvestInfoByPage_Report(tables, report);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析对外投资信息分页--年报
        /// <summary>
        /// 解析对外投资信息分页--年报
        /// </summary>
        /// <param name="tables"></param>
        /// <param name="report"></param>
        void LoadAndParseInvestInfoByPage_Report(HtmlNodeCollection tables, Report report)
        {
            foreach (var table in tables)
            {
                var trs = table.SelectNodes("./tbody/tr");
                if (trs != null && trs.Any() && trs.Count == 2)
                {
                    InvestItem item = new InvestItem();
                    var first = trs.First();
                    var last = trs.Last();

                    var first_td = first.SelectSingleNode("./td");
                    var last_td = last.SelectSingleNode("./td");

                    item.seq_no = report.invest_items.Count + 1;
                    item.invest_name = first_td.InnerText;
                    item.invest_reg_no = last_td.InnerText.Replace("·注册号/统一社会信用代码：", "");
                    report.invest_items.Add(item);
                }
            }
        }
        #endregion

        #region 解析企业对外担保信息
        /// <summary>
        /// 解析企业对外担保信息
        /// </summary>
        /// <param name="rootNode"></param>
        /// <param name="report"></param>
        void LoadAndParseExternalGuaranteeInfo_Report(HtmlNode rootNode, Report report)
        {
            var request = this.CreateRequest();
            var iframe = rootNode.SelectSingleNode("//iframe[@id='dwtzFrame']");
            if (iframe != null)
            {
                var http = "http://gx.gsxt.gov.cn";
                var src = iframe.OuterHtml.Split('\'')[1];
                if (src != null)
                {
                    var url = string.Format("{0}{1}", http, src);
                    var requestList = new List<RequestSetting>();

                    requestList.Add(new RequestSetting()
                    {
                        Name = "ExternalGuarantee_Report",
                        Method = "get",
                        IsArray = "0",
                        Url = url
                    });
                    var responseList = request.GetResponseInfo(requestList);
                    if (responseList != null && responseList.Any())
                    {
                        HtmlDocument document = new HtmlDocument();
                        HtmlNode.ElementsFlags.Remove("form");
                        HtmlNode.ElementsFlags.Remove("input");
                        document.LoadHtml(responseList.First().Data);
                        var rd = document.DocumentNode;
                        var table = rd.SelectSingleNode("//table[@class='table-result']");
                        if (table != null)
                        {
                            this.LoadAndParseExternalGuaranteeInfoByPage_Report(table, report);
                            var form = rd.SelectSingleNode("//form");
                            if (form == null) return;
                            var entidInput = form.ChildNodes.FirstOrDefault(node => node.Name == "input" && node.Attributes.Contains("name") && node.Attributes["name"].Value == "entid");
                            var cidInput = form.ChildNodes.FirstOrDefault(node => node.Name == "input" && node.Attributes.Contains("name") && node.Attributes["name"].Value == "cid");
                            request.AddOrUpdateRequestParameter("report_entid", entidInput.Attributes["value"].Value);
                            request.AddOrUpdateRequestParameter("report_cid", cidInput.Attributes["value"].Value);
                            var pagesDiv = form.SelectSingleNode("div[@class='pages']");
                            if (pagesDiv == null) return;
                            var input = pagesDiv.ChildNodes.FirstOrDefault(node => node.Name == "input" && node.Attributes.Contains("id") && node.Attributes["id"].Value == "pagescount");
                            if (input == null) return;

                            var pages = int.Parse(input.Attributes["value"].Value);
                            request.AddOrUpdateRequestParameter("reportInfo_detail_guarantee_url", src);

                            for (int i = 2; i <= pages; i++)
                            {
                                request.AddOrUpdateRequestParameter("pageNos", i.ToString());
                                request.AddOrUpdateRequestParameter("pageNo", (i - 1).ToString());
                                var resList = request.GetResponseInfo(_requestXml.GetRequestListByName("reportInfo_detail_guarantee"));
                                if (resList != null && resList.Any())
                                {
                                    HtmlDocument documentPage = new HtmlDocument();
                                    documentPage.LoadHtml(resList.First().Data);
                                    var rdPage = documentPage.DocumentNode;
                                    var pageTable = rdPage.SelectSingleNode("//table[@class='table-result']");
                                    this.LoadAndParseExternalGuaranteeInfoByPage_Report(pageTable, report);
                                }
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析企业对外担保信息分页--年报
        /// <summary>
        /// 解析企业对外担保信息分页--年报
        /// </summary>
        /// <param name="rootNode"></param>
        /// <param name="report"></param>
        void LoadAndParseExternalGuaranteeInfoByPage_Report(HtmlNode table, Report report)
        {
            if (table == null) return;
            var trs = table.SelectNodes("./tr");
            if (trs != null && trs.Any())
            {
                foreach (var tr in trs)
                {
                    var tds = tr.SelectNodes("./td");
                    if (tds != null && tds.Any() && tds.Count == 8)
                    {
                        ExternalGuarantee eg = new ExternalGuarantee();
                        eg.seq_no = report.update_records.Count + 1;
                        eg.creditor = tds[0].InnerText;
                        eg.debtor = tds[1].InnerText;
                        eg.type = tds[2].InnerText;
                        eg.amount = tds[3].InnerText;
                        eg.period = tds[4].InnerText;
                        eg.guarantee_time = tds[5].InnerText;
                        eg.guarantee_type = tds[6].InnerText;
                        eg.guarantee_scope = tds[7].InnerText;
                        report.external_guarantees.Add(eg);
                    }
                }
            }
        }
        #endregion

        #region 解析修改信息
        /// <summary>
        /// 解析修改信息
        /// </summary>
        /// <param name="rootNode"></param>
        /// <param name="report"></param>
        void LoadAndParseUpdateRecordInfo_Report(HtmlNode rootNode, Report report)
        {
            var request = this.CreateRequest();
            var iframe = rootNode.SelectSingleNode("//iframe[@id='xgFrame']");
            if (iframe != null)
            {
                var http = "http://gx.gsxt.gov.cn";
                var src = iframe.OuterHtml.Split('\'')[1];
                if (src != null)
                {
                    var url = string.Format("{0}{1}", http, src);
                    var requestList = new List<RequestSetting>();

                    requestList.Add(new RequestSetting()
                    {
                        Name = "UpdateRecord_Report",
                        Method = "get",
                        IsArray = "0",
                        Url = url
                    });
                    var responseList = request.GetResponseInfo(requestList);
                    if (responseList != null && responseList.Any())
                    {
                        HtmlDocument document = new HtmlDocument();
                        HtmlNode.ElementsFlags.Remove("form");
                        HtmlNode.ElementsFlags.Remove("input");
                        document.LoadHtml(responseList.First().Data);
                        var rd = document.DocumentNode;
                        var table = rd.SelectSingleNode("//table");
                        if (table != null)
                        {
                            this.LoadAndParseUpdateRecordInfoByPage_Report(table, report);
                            var form = rd.SelectSingleNode("//form");
                            if (form == null) return;
                            var entidInput = form.ChildNodes.FirstOrDefault(node => node.Name == "input" && node.Attributes.Contains("name") && node.Attributes["name"].Value == "entid");
                            var cidInput = form.ChildNodes.FirstOrDefault(node => node.Name == "input" && node.Attributes.Contains("name") && node.Attributes["name"].Value == "cid");
                            var yearInput = form.ChildNodes.FirstOrDefault(node => node.Name == "input" && node.Attributes.Contains("name") && node.Attributes["name"].Value == "year");
                            request.AddOrUpdateRequestParameter("report_entid", entidInput.Attributes["value"].Value);
                            request.AddOrUpdateRequestParameter("report_cid", cidInput.Attributes["value"].Value);
                            request.AddOrUpdateRequestParameter("report_year", yearInput.Attributes["value"].Value);
                            var pagesDiv = form.SelectSingleNode("div[@class='pages']");
                            if (pagesDiv == null) return;
                            var input = pagesDiv.ChildNodes.FirstOrDefault(node => node.Name == "input" && node.Attributes.Contains("id") && node.Attributes["id"].Value == "pagescount");
                            if (input == null) return;

                            var pages = int.Parse(input.Attributes["value"].Value);
                            request.AddOrUpdateRequestParameter("reportInfo_detail_updaterecord_url", src);

                            for (int i = 2; i <= pages; i++)
                            {
                                request.AddOrUpdateRequestParameter("pageNos", i.ToString());
                                request.AddOrUpdateRequestParameter("pageNo", (i - 1).ToString());
                                var resList = request.GetResponseInfo(_requestXml.GetRequestListByName("reportInfo_detail_updaterecord"));
                                if (resList != null && resList.Any())
                                {
                                    HtmlDocument documentPage = new HtmlDocument();
                                    documentPage.LoadHtml(resList.First().Data);
                                    var rdPage = documentPage.DocumentNode;
                                    var pageTable = rdPage.SelectSingleNode("//table[@class='table-result']");
                                    this.LoadAndParseUpdateRecordInfoByPage_Report(pageTable, report);
                                }
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析修改信息--分页
        /// <summary>
        /// 解析修改信息--分页
        /// </summary>
        /// <param name="rootNode"></param>
        /// <param name="report"></param>
        void LoadAndParseUpdateRecordInfoByPage_Report(HtmlNode table, Report report)
        {
            if (table == null) return;
            var trs = table.SelectNodes("./tr");
            if (trs != null && trs.Any())
            {
                foreach (var tr in trs)
                {
                    var tds = tr.SelectNodes("./td");
                    if (tds != null && tds.Any() && tds.Count == 5)
                    {
                        UpdateRecord ur = new UpdateRecord();
                        ur.seq_no = report.update_records.Count + 1;
                        ur.update_item = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Replace("&nbsp;", "");
                        ur.before_update = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Replace("&nbsp;", "");
                        ur.after_update = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Replace("&nbsp;", "");
                        ur.update_date = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "").Replace("&nbsp;", "");
                        report.update_records.Add(ur);
                    }
                }
            }
        }
        #endregion

        #region 解析股权冻结--司法协助
        /// <summary>
        /// 解析股权冻结--司法协助
        /// </summary>
        /// <param name="rootNode"></param>
        void LoadAndParseJudicialFreezeInfo(HtmlNode rootNode)
        {
            var request = this.CreateRequest();

            var table = rootNode.SelectSingleNode("//table[@class='table-result']");
            if (table != null)
            {
                this.LoadAndParseJudicialFreezeInfoByPage(table);

                var form = rootNode.SelectSingleNode("//form");
                if (form == null) return;
                var pagesDiv = form.SelectSingleNode("div[@class='pages']");
                if (pagesDiv == null) return;
                var input = pagesDiv.ChildNodes.FirstOrDefault(node => node.Name == "input" && node.Attributes.Contains("id") && node.Attributes["id"].Value == "pagescount");
                if (input == null) return;
                var pages = int.Parse(input.Attributes["value"].Value);
                for (int i = 2; i <= pages; i++)
                {
                    request.AddOrUpdateRequestParameter("pageNo", i.ToString());
                    var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("judicial_freezeInfobypage"));
                    if (responseList != null && responseList.Any())
                    {
                        HtmlDocument document = new HtmlDocument();
                        document.LoadHtml(responseList.First().Data);
                        var rd = document.DocumentNode;
                        var pageTable = rd.SelectSingleNode("//table[@class='table-result']");
                        this.LoadAndParseJudicialFreezeInfoByPage(pageTable);
                    }
                }
            }
        }
        #endregion

        #region 解析股权冻结分页--司法协助
        /// <summary>
        /// 解析股权冻结分页--司法协助
        /// </summary>
        /// <param name="table"></param>
        void LoadAndParseJudicialFreezeInfoByPage(HtmlNode table)
        {
            var requst = this.CreateRequest();
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Any() && tds.Count == 7)
                        {
                            JudicialFreeze jf = new JudicialFreeze();
                            jf.seq_no = _enterpriseInfo.judicial_freezes.Count + 1;
                            jf.be_executed_person = tds[1].InnerText;
                            jf.amount = tds[2].InnerText;
                            jf.executive_court = tds[3].InnerText;
                            jf.number = tds[4].InnerText;
                            jf.status = tds[5].InnerText;
                            var a = tds.Last().SelectSingleNode("./a");
                            if (a != null)
                            {
                                var onclick = a.Attributes.Contains("onclick") ? a.Attributes["onclick"].Value : string.Empty;
                                if (!string.IsNullOrWhiteSpace(onclick))
                                {
                                    var arr = onclick.Split('\'');
                                    if (arr != null && arr.Length == 3)
                                    {

                                        requst.AddOrUpdateRequestParameter("judicial_freeze_detail_url", arr[1]);
                                        var responseList = requst.GetResponseInfo(_requestXml.GetRequestListByName("judicial_freeze_detail"));
                                        if (responseList != null && responseList.Any())
                                        {
                                            this.LoadAndParseJudicialFreezeDetailInfo(responseList.First().Data, jf);
                                        }
                                    }
                                }
                            }
                            _enterpriseInfo.judicial_freezes.Add(jf);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析股权冻结详情
        /// <summary>
        /// 解析股权冻结详情
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="jf"></param>
        void LoadAndParseJudicialFreezeDetailInfo(string responseData, JudicialFreeze jf)
        {
            if (!string.IsNullOrWhiteSpace(responseData))
            {
                responseData = responseData.Replace("<thead>", "").Replace("</thead>", "").Replace("<tbody>", "").Replace("</tbody>", "");
            }
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            var table = rootNode.SelectSingleNode("//table[@class='table-result']");
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    foreach (var tr in trs)
                    {
                        var ths = tr.SelectNodes("./th");
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && ths != null && tds.Count == ths.Count && tds.Any())
                        {
                            for (int i = 0; i < ths.Count; i++)
                            {
                                var title = ths[i].InnerText;
                                var val = tds[i].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace("&nbsp;", "").Trim();
                                switch (title)
                                {
                                    case "相关企业名称":
                                    case "股权所在企业名称":
                                        jf.detail.corp_name = val;
                                        break;
                                    case "执行法院":
                                        jf.detail.execute_court = val;
                                        break;
                                    case "执行事项":
                                        jf.detail.assist_item = val;
                                        break;
                                    case "执行裁定书文号":
                                        jf.detail.adjudicate_no = val;
                                        break;
                                    case "执行通知书文号":
                                        jf.detail.notice_no = val;
                                        break;
                                    case "被执行人":
                                        jf.detail.assist_name = val;
                                        break;
                                    case "被执行人证件种类":
                                        jf.detail.assist_ident_type = val;
                                        break;
                                    case "被执行人证件号码":
                                        jf.detail.assist_ident_no = val;
                                        break;
                                    case "被执行人持有股权、其他投资权益的数据额（万元）":
                                    case "被执行人持有股权、其它投资权益的数额":
                                        jf.detail.freeze_amount = val;
                                        break;
                                    case "冻结开始日期":
                                    case "冻结期限自":
                                        jf.detail.freeze_start_date = val;
                                        break;
                                    case "冻结结束日期":
                                    case "冻结期限至":
                                        jf.detail.freeze_end_date = val;
                                        break;
                                    case "冻结期限":
                                        jf.detail.freeze_year_month = val;
                                        break;
                                    case "公示日期":
                                        jf.detail.public_date = val;
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

        #region GetTimeLikeJS
        /// <summary>
        /// GetTimeLikeJS
        /// </summary>
        /// <returns></returns>
        public long GetTimeLikeJS()
        {
            long lLeft = 621355968000000000;
            DateTime dt = DateTime.Now;
            long Sticks = (dt.Ticks - lLeft) / 10000;
            return Sticks;
        }
        #endregion
    }
}
