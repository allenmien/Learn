using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Reflection;
using System.Collections;
using System.Xml.Linq;
using System.Web.UI;

using Newtonsoft.Json;
using iOubo.iSpider.Model;
using HtmlAgilityPack;
using iOubo.iSpider.Common;

namespace iOubo.iSpider.Infrastructure.Parser
{
    public class ConverterCN : IConverter
    {
        DataRequest _request;
        RequestInfo _requestInfo;
        RequestXml _requestXml;
        EnterpriseInfo _enterpriseInfo = new EnterpriseInfo();
        List<AbnormalInfo> _abnormals = new List<AbnormalInfo>();
        List<CheckupInfo> _checkups = new List<CheckupInfo>();
        Dictionary<string, string> dicInvtType = new Dictionary<string, string>();

        public SummaryEntity ProcessRequestAndParse(RequestInfo requestInfo)
        {
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
            InitCommonData();
            //解析基本信息：基本信息、股东信息、变更信息、主要人员信息、分支机构信息、经营异常信息、抽查检查信息
            List<ResponseInfo> responseList = GetResponseInfo(_requestXml.GetRequestListByName("gongshang"));
            ParseResponse(responseList);

            //解析年报
            responseList = GetResponseInfo(_requestXml.GetRequestListByName("report"));
            ParseResponse(responseList);
            //股权冻结
            responseList = GetResponseInfo(_requestXml.GetRequestListByName("sifaxiezhu"));
            ParseResponse(responseList);
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

        private List<ResponseInfo> GetResponseInfo(IEnumerable<XElement> elements)
        {
            List<ResponseInfo> responseList = new List<ResponseInfo>();
            foreach (XElement el in elements)
            {
                responseList.Add(this._request.RequestData(el));
            }

            return responseList;
        }

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
                        LoadAndParseTab01(responseInfo.Data, _enterpriseInfo);
                        break;
                    case "report":
                        LoadAndParseReport(responseInfo.Data, _enterpriseInfo);
                        LoadAndParseGuDongJiChuZi(responseInfo.Data, _enterpriseInfo);
                        LoadAndParseGuQuanBianGeng(responseInfo.Data, _enterpriseInfo);
                        LoadAndParseXingZhengXuKe(responseInfo.Data, _enterpriseInfo);
                        LoadAndParseZhiShiChanQuan(responseInfo.Data, _enterpriseInfo);

                        break;
                    case "sifaxiezhu":
                        LoadAndParseGuQuanDongjie(responseInfo.Data,_enterpriseInfo);
                        break;
                    default:
                        break;
                }
            }
        }

        /// <summary>
        /// 解析工商公示信息：基本信息、股东信息、变更信息、主要人员信息、分支机构信息、经营异常信息、抽查检查信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseTab01(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            string name = string.Empty;
            // 基本信息、股东信息、变更信息
            HtmlNodeCollection divs = rootNode.SelectNodes("//div[@rel='layout-01_01']");
            foreach (HtmlNode div in divs)
            {
                HtmlNode table = div.SelectSingleNode("./table");

                string title = table.SelectNodes("./tr/th")[0].InnerText.Trim();
                if (title == "基本信息")
                {
                    HtmlNodeCollection tdList = table.SelectNodes("./tr/td");
                    HtmlNodeCollection thList = table.SelectNodes("./tr/th");
                    for (int i = 1; i < thList.Count; i++)
                    {
                        switch (thList[i].InnerText.Trim())
                        {
                            case "注册号":
                                _enterpriseInfo.reg_no = tdList[i - 1].InnerText.Trim().Replace("&nbsp;", "");
                                break;
                            case "统一社会信用代码":
                                _enterpriseInfo.credit_no = tdList[i - 1].InnerText.Trim().Replace("&nbsp;", "");
                                break;
                            case "注册号/统一社会信用代码":
                            case "统一社会信用代码/注册号":
                                if (tdList[i - 1].InnerText.Trim().Replace("&nbsp;", "").Length == 18)
                                    _enterpriseInfo.credit_no = tdList[i - 1].InnerText.Trim().Replace("&nbsp;", "");
                                else
                                    _enterpriseInfo.reg_no = tdList[i - 1].InnerText.Trim().Replace("&nbsp;", "");
                                break;
                            case "名称":
                                _enterpriseInfo.name = tdList[i - 1].InnerText.Trim().Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                                name = _enterpriseInfo.name;
                                break;
                            case "类型":
                                _enterpriseInfo.econ_kind = tdList[i - 1].InnerText.Trim().Replace("null", "").Replace("NULL", "");
                                break;
                            case "法定代表人":
                            case "负责人":
                            case "股东":
                            case "经营者":
                            case "执行事务合伙人":
                            case "投资人":
                                _enterpriseInfo.oper_name = tdList[i - 1].InnerText.Trim().Replace("null", "").Replace("NULL", "");
                                break;
                            case "住所":
                            case "经营场所":
                            case "营业场所":
                            case "主要经营场所":
                                Address address = new Address();
                                address.name = "注册地址";
                                address.address = tdList[i - 1].InnerText.Trim();
                                address.postcode = "";
                                _enterpriseInfo.addresses.Add(address);
                                break;
                            case "注册资本":
                                _enterpriseInfo.regist_capi = tdList[i - 1].InnerText.Trim();
                                break;
                            case "成立日期":
                            case "注册日期":
                                _enterpriseInfo.start_date = tdList[i - 1].InnerText.Trim();
                                break;
                            case "营业期限自":
                            case "经营期限自":
                            case "合伙期限自":
                                _enterpriseInfo.term_start = tdList[i - 1].InnerText.Trim();
                                break;
                            case "营业期限至":
                            case "经营期限至":
                            case "合伙期限至":
                                _enterpriseInfo.term_end = tdList[i - 1].InnerText.Trim();
                                break;
                            case "经营范围":
                                _enterpriseInfo.scope = tdList[i - 1].InnerText.Trim().Replace("null", "").Replace("NULL", "");
                                break;
                            case "登记机关":
                                _enterpriseInfo.belong_org = tdList[i - 1].InnerText.Trim();
                                break;
                            case "核准日期":
                                _enterpriseInfo.check_date = tdList[i - 1].InnerText.Trim();
                                break;
                            case "登记状态":
                                _enterpriseInfo.status = tdList[i - 1].InnerText.Trim();
                                break;
                            case "吊销日期":
                            case "注销日期":
                                _enterpriseInfo.end_date = tdList[i - 1].InnerText.Trim();
                                break;
                            default:
                                break;
                        }
                    }
                }
                else if (title == "变更信息")
                {
                    HtmlNodeCollection trList = table.SelectNodes("./tr");

                    int k = 1;
                    List<ChangeRecord> changeRecordList = new List<ChangeRecord>();
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tddList = rowNode.SelectNodes("./td");
                        ChangeRecord changeRecord = new ChangeRecord();
                        if (tddList != null && tddList.Count > 3)
                        {
                            changeRecord.change_item = tddList[0].InnerText;
                            changeRecord.before_content = tddList[1].InnerText;
                            changeRecord.after_content = tddList[2].InnerText;
                            changeRecord.change_date = tddList[3].InnerText;
                            changeRecord.seq_no = k++;
                            changeRecordList.Add(changeRecord);
                        }
                    }
                    _enterpriseInfo.changerecords = changeRecordList;
                }
                else
                {
                    // 股东信息
                    HtmlNodeCollection trList = table.SelectNodes("./tr");

                    List<Partner> partnerList = new List<Partner>();
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                        if (tdList != null && tdList.Count > 3)
                        {
                            Partner partner = new Partner();
                            partner.identify_no = tdList[3].InnerText.Trim();
                            partner.identify_type = tdList[2].InnerText.Trim();
                            var uuid = string.Empty;
                            if (tdList.Count > 4)
                            {
                                var aNode = tdList[4].SelectSingleNode("./a");
                                if (aNode != null)
                                    uuid = Regex.Split(tdList[4].SelectSingleNode("./a").Attributes["href"].Value, "uuid=")[1];
                            }
                            partner.ex_id = uuid;
                            partner.seq_no = partnerList.Count + 1;
                            partner.stock_name = tdList[1].InnerText.Trim();
                            partner.stock_percent = "";
                            partner.stock_type = tdList[0].InnerText.Trim();
                            partner.should_capi_items = new List<ShouldCapiItem>();
                            partner.real_capi_items = new List<RealCapiItem>();

                            // 解析股东详情
                            _request.AddOrUpdateRequestParameter("investorId", uuid);
                            List<ResponseInfo> reponseList = GetResponseInfo(_requestXml.GetRequestListByName("investor_detials"));
                            if (reponseList.Count() > 0)
                            {
                                LoadAndParseInvestorDetails(partner, reponseList[0].Data);
                            }

                            partnerList.Add(partner);
                        }
                    }
                    _enterpriseInfo.partners = partnerList;
                }
            }

            //主要人员信息、分支机构信息
            divs = rootNode.SelectNodes("//div[@rel='layout-01_02']");
            if (divs != null)
            {
                foreach (HtmlNode div in divs)
                {
                    HtmlNode table = div.SelectSingleNode("./table");

                    string title = table.SelectNodes("./tr/th")[0].InnerText.Trim();
                    if (title == "主要人员信息")
                    {
                        HtmlNodeCollection trList = table.SelectNodes("./tr");

                        int i = 1;
                        List<Employee> employeeList = new List<Employee>();
                        foreach (HtmlNode rowNode in trList)
                        {
                            HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                            if (tdList != null && tdList.Count > 2)
                            {
                                Employee employee = new Employee();
                                employee.seq_no = i++;
                                employee.name = tdList[1].InnerText;
                                employee.job_title = tdList[2].InnerText;
                                employee.cer_no = "";

                                employeeList.Add(employee);

                                if (tdList.Count > 5 && tdList[4].InnerText != "")
                                {
                                    Employee employee2 = new Employee();
                                    employee2.seq_no = i++;
                                    employee2.name = tdList[4].InnerText;
                                    employee2.job_title = tdList[5].InnerText;
                                    employee2.cer_no = "";

                                    employeeList.Add(employee2);
                                }
                            }
                        }

                        _enterpriseInfo.employees = employeeList;
                    }
                    else if (title == "分支机构信息")
                    {

                        HtmlNodeCollection trList = table.SelectNodes("./tr");

                        int i = 1;
                        List<Branch> branchList = new List<Branch>();
                 
                            foreach (HtmlNode rowNode in trList)
                            {
                                HtmlNodeCollection tdList = rowNode.SelectNodes("./td");

                                if (tdList != null && tdList.Count > 3)
                                {
                                    Branch branch = new Branch();
                                    branch.seq_no = i++;
                                    branch.belong_org = tdList[3].InnerText;
                                    branch.name = tdList[2].InnerText;
                                    branch.oper_name = "";
                                    if (name != "中国银行股份有限公司")
                                    {
                                        branch.reg_no = tdList[1].InnerText;
                                    }

                                    branchList.Add(branch);
                                }
                            }
                    

                        _enterpriseInfo.branches = branchList;
                    }
                }
            }


            // 经营异常信息
            HtmlNode yichangDiv = rootNode.SelectSingleNode("//div[@rel='layout-01_05']");
            HtmlNode yichangTable = yichangDiv.SelectSingleNode("./table");
            HtmlNodeCollection yichangTrList = yichangTable.SelectNodes("./tr");
            foreach (HtmlNode rowNode in yichangTrList)
            {
                HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                if (tdList != null && tdList.Count > 3)
                {
                    AbnormalInfo item = new AbnormalInfo();
                    item.in_reason = tdList[1].InnerText;
                    item.in_date = tdList[2].InnerText;
                    item.out_reason = tdList[3].InnerText;
                    item.out_date = tdList[4].InnerText;
                    item.department = tdList[5].InnerText;

                    _abnormals.Add(item);
                }
            }

            // 抽查检查信息
            HtmlNode jianchaDiv = rootNode.SelectSingleNode("//div[@rel='layout-01_08']");
            HtmlNode jianchaTable = jianchaDiv.SelectSingleNode("./table");
            HtmlNodeCollection jianchaTrList = jianchaTable.SelectNodes("./tr");
            foreach (HtmlNode rowNode in jianchaTrList)
            {
                HtmlNodeCollection tdList = rowNode.SelectNodes("./td");

                if (tdList != null && tdList.Count > 3)
                {
                    CheckupInfo item = new CheckupInfo();
                    item.department = tdList[1].InnerText;
                    item.type = tdList[2].InnerText;
                    item.date = tdList[3].InnerText;
                    item.result = tdList[4].InnerText;

                    _checkups.Add(item);
                }
            }
            //股权出质
            HtmlNode guquanchuzhiTable = rootNode.SelectSingleNode("//table[@id='pledgeTable']");
            if (guquanchuzhiTable != null)
            {
                var trs = guquanchuzhiTable.SelectNodes("./tr");
                if (trs != null && trs.Count > 1)
                {
                    trs.Remove(0);
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Count > 9)
                        {
                            EquityQuality euityQuality = new EquityQuality();
                            euityQuality.seq_no = int.Parse(tds[0].InnerText);
                            euityQuality.number = tds[1].InnerText;
                            euityQuality.pledgor = tds[2].InnerText;
                            euityQuality.pledgor_identify_type = "";
                            euityQuality.pledgor_identify_no = tds[3].InnerText;
                            euityQuality.pledgor_amount = tds[4].InnerText;
                            euityQuality.pledgor_currency = "";
                            euityQuality.pawnee = tds[5].InnerText;
                            euityQuality.pawnee_identify_type = "";
                            euityQuality.pawnee_identify_no = tds[6].InnerText;
                            euityQuality.date = tds[7].InnerText;
                            euityQuality.status = tds[8].InnerText;
                            var a = tds[9].SelectSingleNode("./a");
                            if (a != null)
                            {
                                var url = a.Attributes.Contains("href") ? a.Attributes["href"].Value : "";
                                if (!string.IsNullOrEmpty(url))
                                {
                                    _request.AddOrUpdateRequestParameter("guquanbiangengUrl",url);
                                    var responseList = _request.GetResponseInfo(_requestXml.GetRequestListByName("guquanchuzhibiangeng"));
                                    if(responseList!=null&&responseList.Any())
                                    {
                                        LoadAndParseGQCZBianGeng(responseList[0].Data,euityQuality);
                                    }
                                }
                            }
                            _enterpriseInfo.equity_qualities.Add(euityQuality);
                        }
                    }
                }
            }
        }
        void LoadAndParseGQCZBianGeng(string responseData,EquityQuality euityQuality)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            var table = rootNode.SelectSingleNode("//table[@id='alterTable']");
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Count > 2)
                {
                    foreach(var tr in trs)
                    {
                        var tds=tr.SelectNodes("./td");
                        if (tds != null && tds.Count > 2)
                        {
                            ChangeItem ci = new ChangeItem();
                            ci.seq_no = euityQuality.change_items.Count() + 1;
                            ci.change_date = tds[1].InnerText;
                            ci.change_content = tds[2].InnerText;
                            euityQuality.change_items.Add(ci);
                        }
                    }
                    
                }
            }
        }
        /// <summary>
        /// 解析股东详情
        /// </summary>
        /// <param name="partner"></param>
        /// <param name="responseData"></param>
        private void LoadAndParseInvestorDetails(Partner partner, String responseData)
        {

            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);

            HtmlNode rootNode = document.DocumentNode;
            HtmlNode infoTable = rootNode.SelectSingleNode("//table[@id='investor']");
            if (infoTable != null)
            {
                HtmlNodeCollection trList = infoTable.SelectNodes("//tr");

                foreach (HtmlNode rowNode in trList)
                {
                    HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                    if (tdList != null && tdList.Count > 8)
                    {
                        ShouldCapiItem sItem = new ShouldCapiItem();
                        var shoudCapi = tdList[4].InnerText.Trim() == "" ? tdList[1].InnerText.Trim() : tdList[4].InnerText.Trim();
                        sItem.shoud_capi = string.IsNullOrEmpty(shoudCapi) ? "" : shoudCapi;
                        sItem.should_capi_date = tdList[5].InnerText.Trim();
                        sItem.invest_type = tdList[3].InnerText.Trim();
                        partner.should_capi_items.Add(sItem);

                        RealCapiItem rItem = new RealCapiItem();
                        var realCapi = tdList[7].InnerText.Trim() == "" ? tdList[2].InnerText.Trim() : tdList[7].InnerText.Trim();
                        rItem.real_capi = string.IsNullOrEmpty(realCapi) ? "" : realCapi ;
                        rItem.real_capi_date = tdList[8].InnerText.Trim();
                        rItem.invest_type = tdList[6].InnerText.Trim();
                        partner.real_capi_items.Add(rItem);
                    }
                }
                if (partner.should_capi_items.Count == 0 && partner.real_capi_items.Count == 0)
                {
                    //投资人放在JavaScript中，需要特殊处理
                    var sIndex = responseData.IndexOf("var investor = new Object();");
                    var eIndex = responseData.IndexOf("generateInvestor(investor);");
                    if (sIndex != -1 && eIndex != -1)
                    {
                        var content = responseData.Substring(sIndex, eIndex - sIndex);
                        var arr = content.Split(';');
                        ShouldCapiItem sItem = null;
                        RealCapiItem rItem = null;
                        for (int i = 0; i < arr.Length; i++)
                        {
                            var tmp = arr[i].Split('=');
                            if (tmp.Length > 1)
                            {
                                if (!(tmp[0].Contains("invt.subConAm ") ||
                                    tmp[0].Contains("invt.conDate ") ||
                                    tmp[0].Contains("invt.conForm ") ||
                                    tmp[0].Contains("invtActl.acConAm ") ||
                                    tmp[0].Contains("invtActl.conDate ") ||
                                    tmp[0].Contains("invtActl.conForm ")))
                                    continue;
                                if (tmp[0].Contains("invt.subConAm "))
                                {
                                    sItem = new ShouldCapiItem();
                                    partner.should_capi_items.Add(sItem);
                                    sItem.shoud_capi = string.IsNullOrEmpty(tmp[1]) ? "" : tmp[1].Trim().Trim(new char[] { '"' });
                                }
                                else if (tmp[0].Contains("invt.conDate "))
                                {
                                    sItem.should_capi_date = tmp[1].Trim().Trim(new char[] { '\'' });
                                }
                                else if (tmp[0].Contains("invt.conForm "))
                                {
                                    sItem.invest_type = tmp[1].Trim().Trim(new char[] { '"' });

                                }
                                else if (tmp[0].Contains("invtActl.acConAm "))
                                {
                                    rItem = new RealCapiItem();
                                    partner.real_capi_items.Add(rItem);
                                    rItem.real_capi = string.IsNullOrEmpty(tmp[1]) ? "" : tmp[1].Trim().Trim(new char[] { '"' });
                                }
                                else if (tmp[0].Contains("invtActl.conDate "))
                                {
                                    rItem.real_capi_date = tmp[1].Trim().Trim(new char[] { '\'' });
                                }
                                else if (tmp[0].Contains("invtActl.conForm "))
                                {
                                    rItem.invest_type = tmp[1].Trim().Trim(new char[] { '"' });
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 解析年报
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseReport(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            try
            {
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;

                HtmlNode div = rootNode.SelectSingleNode("//div[@rel='layout-02_01']");
                HtmlNode table = div.SelectSingleNode("./table");
                HtmlNodeCollection trList = table.SelectNodes("./tr");

                List<Report> reportList = new List<Report>();
                foreach (HtmlNode rowNode in trList)
                {
                    HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                    if (tdList != null && tdList.Count > 2)
                    {
                        Report report = new Report();
                        string reportHerf = tdList[1].Element("a").Attributes["href"].Value;
                        string reportId = Regex.Split(reportHerf, "uuid=")[1];
                        report.report_year = tdList[1].InnerText.Trim().Length > 4 ? tdList[1].InnerText.Trim().Substring(0, 4) : "";
                        report.report_date = tdList[2].InnerText;

                        // 加载解析年报详细信息
                        _request.AddOrUpdateRequestParameter("reportId", reportId);
                        List<ResponseInfo> responseList = GetResponseInfo(_requestXml.GetRequestListByName("reportDetail"));
                        if (responseList != null && responseList.Count > 0)
                        {
                            LoadAndParseReportDetail(responseList[0].Data, report);
                        }
                        reportList.Add(report);
                    }
                }
                _enterpriseInfo.reports = reportList;
            }
            catch (Exception ex)
            {
                _enterpriseInfo.reports.Clear();
                Console.WriteLine("Exception when LoadAndParseReport.." + ex.ToString());
                LogHelper.Error("Exception when LoadAndParseReport.." + ex.ToString());
            }
        }

        /// <summary>
        /// 加载解析年报详细信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportDetail(string responseData, Report report)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            HtmlNodeCollection tables = rootNode.SelectNodes("//table[@class='info m-bottom m-top']");
            foreach (HtmlNode table in tables)
            {
                string title = table.SelectNodes("./tr/th")[0].InnerText.Trim();
                if (title.EndsWith("年度年度报告&nbsp;红色为修改过的信息项"))
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
                                        report.name = tdList[i].InnerText.Trim().Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                                        break;
                                    case "企业联系电话":
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
                                    default:
                                        break;
                                }
                            }
                        }
                    }
                }
                else if (title == "网站或网店信息")
                {
                    HtmlNodeCollection trList = table.SelectNodes("./tr");

                    List<WebsiteItem> websiteList = new List<WebsiteItem>();
                    int j = 1;
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                        if (tdList != null && tdList.Count > 2)
                        {
                            WebsiteItem item = new WebsiteItem();

                            item.seq_no = j++;
                            item.web_type = tdList[0].InnerText;
                            item.web_name = tdList[1].InnerText;
                            item.web_url = tdList[2].InnerText;

                            websiteList.Add(item);
                        }
                    }
                    report.websites = websiteList;
                }
                else if (title.Contains("股东及出资信息"))
                {
                    HtmlNodeCollection trList = table.SelectNodes("./tr");

                    List<Partner> partnerList = new List<Partner>();
                    int j = 1;
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                        if (tdList != null && tdList.Count > 5)
                        {
                            Partner item = new Partner();

                            item.seq_no = j++;
                            item.stock_name = tdList[0].InnerText;
                            item.stock_type = tdList[6].InnerText;
                            item.identify_no = "";
                            item.identify_type = "";
                            item.stock_percent = "";
                            item.ex_id = "";
                            item.real_capi_items = new List<RealCapiItem>();
                            item.should_capi_items = new List<ShouldCapiItem>();

                            ShouldCapiItem sItem = new ShouldCapiItem();
                            var shoudCapi = tdList[1].InnerText.Trim();
                            sItem.shoud_capi = string.IsNullOrEmpty(shoudCapi) ? "" : shoudCapi;
                            sItem.should_capi_date = tdList[2].InnerText.Trim();
                            sItem.invest_type = tdList[3].InnerText.Trim();
                            item.should_capi_items.Add(sItem);

                            RealCapiItem rItem = new RealCapiItem();
                            var realCapi = tdList[4].InnerText.Trim();
                            rItem.real_capi = string.IsNullOrEmpty(realCapi) ? "" : realCapi;
                            rItem.real_capi_date = tdList[5].InnerText.Trim();
                            rItem.invest_type = tdList[6].InnerText.Trim();
                            item.real_capi_items.Add(rItem);

                            partnerList.Add(item);
                        }
                    }
                    report.partners = partnerList;
                }
                else if (title == "对外投资信息")
                {
                    HtmlNodeCollection trList = table.SelectNodes("./tr");

                    List<InvestItem> investList = new List<InvestItem>();
                    int j = 1;
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                        if (tdList != null && tdList.Count > 1)
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
                else if (title == "企业资产状况信息")
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
                    HtmlNodeCollection trList = table.SelectNodes("./tr");
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
                else if (title.Contains("对外提供保证担保信息"))
                {
                    var trList = table.SelectNodes("./tr");
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


        /// <summary>
        /// 去掉字符串的首尾
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        private string removeHeadAndTail(string target)
        {
            target = target.Trim();
            return target.Substring(1, target.Length - 2);
        }

        void InitCommonData()
        {

            dicInvtType["1"] = "货币";

            dicInvtType["2"] = "实物";

            dicInvtType["3"] = "知识产权";

            dicInvtType["4"] = "债权转股权出资";

            dicInvtType["5"] = "高新技术成果";

            dicInvtType["6"] = "土地使用权";

            dicInvtType["7"] = "股权";

            dicInvtType["9"] = "其他";

            dicInvtType["0"] = "非货币";

            dicInvtType["Z"] = "货币,非货币";

        }
        /// <summary>
        /// 获取股东及出资信息
        /// </summary>
        /// <param name="html"></param>
        /// <returns></returns>
        List<FinancialContribution> GetFinancialContributionList(string html)
        {
            List<FinancialContribution> list = new List<FinancialContribution>();
            var regexStr = string.Format(@"\r\n\t\tinvestor.inv = {0}(?<investor>[\sA-Za-z（）\u4e00-\u9fa5]*){0};\r\n\t\tinvestor.invType = {0}{0};\r\n\t\t\r\n\t\t//认缴\r\n\t\tvar entOthInvtSet = new Array{1}{2};\r\n\t\t\r\n\t\t\tvar invt = new Object{1}{2};\r\n\t\t\tinvt.subConAm = {0}(?<invtSubConAm>[0-9]\d*\.?\d*){0};\r\n\t\t\tinvt.conDate = '(?<invtConDate>[0-9]+年[0-9]+月[0-9]+日)';\r\n\t\t\tinvt.conForm = {0}(?<invtConForm>[1-9]+){0};\r\n\t\t\tentOthInvtSet.push{1}invt{2};\r\n\t\t\r\n\t\t\r\n\t\t//实缴\r\n\t\tvar entOthInvtactlSet = new Array{1}{2};(?<SHIJIAO>\r\n\t\t\r\n\t\t\tvar invtActl = new Object{1}{2};\r\n\t\t\tinvtActl.acConAm =  {0}(?<acConAm>[0-9]+.?[0-9]*){0};\r\n\t\t\tinvtActl.conDate = '(?<invtActlConDate>[0-9]+年[0-9]+月[0-9]+日)';\r\n\t\t\tinvtActl.conForm = {0}(?<invtActlConForm>[1-9]+){0};\r\n\t\t\tentOthInvtactlSet.push{1}invtActl{2};)?", "\"", "\\(", "\\)");
            var regex = new Regex(regexStr);


            if (regex.IsMatch(html))
            {
                var matches = regex.Matches(html);
                var c = 1;
                foreach (Match match in matches)
                {
                    FinancialContribution fc = new FinancialContribution();
                    fc.seq_no = c;
                    fc.investor_name = match.Groups["investor"].Value;
                    fc.total_should_capi = string.IsNullOrWhiteSpace(match.Groups["invtSubConAm"].Value) ? "0" : match.Groups["invtSubConAm"].Value;
                    fc.total_real_capi = string.IsNullOrWhiteSpace(match.Groups["acConAm"].Value) ? "0" : match.Groups["acConAm"].Value;
                    FinancialContribution.ShouldCapiItem sci = new FinancialContribution.ShouldCapiItem();
                    sci.should_capi = string.IsNullOrWhiteSpace(match.Groups["invtSubConAm"].Value) ? "" : match.Groups["invtSubConAm"].Value;
                    sci.should_invest_date = match.Groups["invtConDate"].Value;
                    if (dicInvtType.ContainsKey(match.Groups["invtConForm"].Value))
                    {
                        sci.should_invest_type = dicInvtType[match.Groups["invtConForm"].Value];
                    }
                    Utility.ClearNullValue<FinancialContribution.ShouldCapiItem>(sci);
                    fc.should_capi_items.Add(sci);
                    FinancialContribution.RealCapiItem rci = new FinancialContribution.RealCapiItem();
                    rci.real_capi = string.IsNullOrWhiteSpace(match.Groups["acConAm"].Value) ? "" : match.Groups["acConAm"].Value;
                    rci.real_invest_date = match.Groups["invtActlConDate"].Value;
                    if (dicInvtType.ContainsKey(match.Groups["invtActlConForm"].Value))
                    {
                        rci.real_invest_type = dicInvtType[match.Groups["invtActlConForm"].Value];
                    }
                    Utility.ClearNullValue<FinancialContribution.RealCapiItem>(rci);
                    fc.real_capi_items.Add(rci);
                    list.Add(fc);
                    c++;
                }

            }
            return list;

        }
        /// <summary>
        /// 加载股东及出资信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        void LoadAndParseGuDongJiChuZi(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            var div = rootNode.SelectSingleNode("//div[@rel='layout-02_04']");
            if (div != null)
            {
                var tables = div.SelectNodes("//table[@id='investor']");
                if (tables != null && tables.Any())
                {
                    var tableFirst = tables.First();
                    var trs = tableFirst.SelectNodes("./tr");
                    if (trs != null && trs.Count > 3)
                    {
                        trs.Remove(0);
                        trs.Remove(0);
                        trs.Remove(0);
                        foreach (var tr in trs)
                        {
                            var tds = tr.SelectNodes("./td");
                            if (tds != null && tds.Count > 8)
                            {
                                FinancialContribution fc = new FinancialContribution();
                                fc.seq_no = _enterpriseInfo.financial_contributions.Count + 1;
                                fc.investor_name = Regex.Replace(tds[0].InnerText, "\\s+(&nbsp;)*", "");
                                fc.total_should_capi = Regex.Replace(tds[1].InnerText, "\\s+(&nbsp;)*", "");
                                fc.total_real_capi = Regex.Replace(tds[2].InnerText, "\\s+(&nbsp;)*", "");

                                FinancialContribution.ShouldCapiItem sci = new FinancialContribution.ShouldCapiItem();
                                sci.should_invest_type = Regex.Replace(tds[3].InnerText, "\\s+(&nbsp;)*", "");
                                sci.should_capi = Regex.Replace(tds[4].InnerText, "\\s+(&nbsp;)*", "");
                                sci.should_invest_date = Regex.Replace(tds[5].InnerText, "\\s+(&nbsp;)*", "");
                                fc.should_capi_items.Add(sci);

                                FinancialContribution.RealCapiItem rci = new FinancialContribution.RealCapiItem();
                                rci.real_invest_type = Regex.Replace(tds[6].InnerText, "\\s+(&nbsp;)*", "");
                                rci.real_capi = Regex.Replace(tds[7].InnerText, "\\s+(&nbsp;)*", "");
                                rci.real_invest_date = Regex.Replace(tds[8].InnerText, "\\s+(&nbsp;)*", "");
                                fc.real_capi_items.Add(rci);

                                _enterpriseInfo.financial_contributions.Add(fc);
                            }
                        }
                    }
                    if (_enterpriseInfo.financial_contributions.Count == 0)
                    {
                        _enterpriseInfo.financial_contributions = GetFinancialContributionList(responseData);
                    }

                    //变更信息
                    if (tables.Count > 1)
                    {
                        var tableSceond = tables[1];
                        trs = tableSceond.SelectNodes("./tr");
                        if (trs != null && trs.Count > 2)
                        {
                            foreach (var tr in trs)
                            {
                                var tds = tr.SelectNodes("./td");
                                if (tds != null && tds.Count > 4)
                                {
                                    UpdateRecord ur = new UpdateRecord();
                                    ur.seq_no = _enterpriseInfo.update_records.Count + 1;
                                    ur.update_item = tds[1].InnerText;
                                    ur.update_date = tds[2].InnerText;
                                    ur.before_update = tds[3].InnerText;
                                    ur.after_update = tds[4].InnerText;
                                    _enterpriseInfo.update_records.Add(ur);
                                }
                            }
                        }
                    }
                }
            }
        }
        /// <summary>
        /// 加载股权变更
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        void LoadAndParseGuQuanBianGeng(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var div = rootNode.SelectSingleNode("//div[@rel='layout-02_06']");
            if (div != null)
            {
                var table = div.SelectSingleNode("./table[@id='othStocktransTable']");
                if (table != null)
                {
                    var trs = table.SelectNodes("./tr");
                    if (trs != null && trs.Count > 2)
                    {
                        foreach (var tr in trs)
                        {
                            var tds = tr.SelectNodes("./td");
                            if (tds != null && tds.Count > 4)
                            {

                                StockChangeItem scItem = new StockChangeItem();
                                scItem.seq_no = int.Parse(Regex.Replace(tds[0].InnerText, "\\s+(&nbsp;)*", ""));
                                scItem.name = Regex.Replace(tds[1].InnerText, "\\s+(&nbsp;)*", "");
                                scItem.before_percent = Regex.Replace(tds[2].InnerText, "\\s+(&nbsp;)*", "");
                                scItem.after_percent = Regex.Replace(tds[3].InnerText, "\\s+(&nbsp;)*", "");
                                scItem.change_date = Regex.Replace(tds[4].InnerText, "\\s+(&nbsp;)*", "");
                                _enterpriseInfo.stock_changes.Add(scItem);
                            }
                        }
                    }
                }
            }
        }
        /// <summary>
        /// 行政许可信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        void LoadAndParseXingZhengXuKe(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            var div = rootNode.SelectSingleNode("//div[@rel='layout-02_02']");
            if (div != null)
            {
                var table = div.SelectSingleNode("./table");
                if (table != null)
                {
                    var trs = table.SelectNodes("./tr");
                    if (trs != null && trs.Count > 0)
                    {
                        foreach (var tr in trs)
                        {
                            var tds = tr.SelectNodes("./td");
                            if (tds != null && tds.Count > 8)
                            {
                                LicenseInfo license = new LicenseInfo();
                                license.seq_no = int.Parse(Regex.Replace(tds[0].InnerText, "\\s+(&nbsp;)*", ""));
                                license.number = Regex.Replace(tds[1].InnerText, "\\s+(&nbsp;)*", "");
                                license.name = Regex.Replace(tds[2].InnerText, "\\s+(&nbsp;)*", "");
                                license.start_date = Regex.Replace(tds[3].InnerText, "\\s+(&nbsp;)*", "");
                                license.end_date = Regex.Replace(tds[4].InnerText, "\\s+(&nbsp;)*", "");
                                license.department = Regex.Replace(tds[5].InnerText, "\\s+(&nbsp;)*", "");
                                license.content = Regex.Replace(tds[6].InnerText, "\\s+(&nbsp;)*", "");
                                license.status = Regex.Replace(tds[7].InnerText, "\\s+(&nbsp;)*", "");
                                _enterpriseInfo.licenses.Add(license);
                            }
                        }
                    }
                }
            }
        }
        /// <summary>
        /// 知识产权信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        void LoadAndParseZhiShiChanQuan(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            var div = rootNode.SelectSingleNode("//div[@rel='layout-02_03']");
            if (div != null)
            {
                var table = div.SelectSingleNode("./table");
                if (table != null)
                {
                    var trs = table.SelectNodes("./tr");
                    if (trs != null && trs.Count > 0)
                    {
                        foreach (var tr in trs)
                        {
                            var tds = tr.SelectNodes("./td");
                            if (tds != null && tds.Count > 8)
                            {
                                KnowledgeProperty kp = new KnowledgeProperty();
                                kp.seq_no = int.Parse(Regex.Replace(tds[0].InnerText, "\\s+(&nbsp;)*", ""));
                                kp.number = Regex.Replace(tds[1].InnerText, "\\s+(&nbsp;)*", "");
                                kp.name = Regex.Replace(tds[2].InnerText, "\\s+(&nbsp;)*", "");
                                kp.type = Regex.Replace(tds[3].InnerText, "\\s+(&nbsp;)*", "");
                                kp.pledgor = Regex.Replace(tds[4].InnerText, "\\s+(&nbsp;)*", "");
                                kp.pawnee = Regex.Replace(tds[5].InnerText, "\\s+(&nbsp;)*", "");
                                kp.period = Regex.Replace(tds[6].InnerText, "\\s+(&nbsp;)*", "");
                                kp.status = Regex.Replace(tds[7].InnerText, "\\s+(&nbsp;)*", "");
                                _enterpriseInfo.knowledge_properties.Add(kp);
                            }
                        }
                    }
                }
            }
        }

        void LoadAndParseGuQuanDongjie(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            var div = rootNode.SelectSingleNode("//div[@rel='layout-06_01']");
            if (div != null)
            {
                var table = div.SelectSingleNode("./table");
                if (table != null)
                {
                    var trs = table.SelectNodes("./tr");
                    if (trs != null && trs.Count > 2)
                    {
                        foreach (var tr in trs)
                        {
                            var tds = tr.SelectNodes("./td");
                            if (tds != null && tds.Count > 6)
                            {
                                JudicialFreeze jf = new JudicialFreeze()
                                {
                                    seq_no = int.Parse(Regex.Replace(tds[0].InnerText, "\\s+(&nbsp;)*", "")),
                                    be_executed_person = Regex.Replace(tds[1].InnerText, "\\s+(&nbsp;)*", ""),
                                    amount = Regex.Replace(tds[2].InnerText, "\\s+(&nbsp;)*", ""),
                                    executive_court = Regex.Replace(tds[3].InnerText, "\\s+(&nbsp;)*", ""),
                                    number = Regex.Replace(tds[4].InnerText, "\\s+(&nbsp;)*", ""),
                                    status = Regex.Replace(tds[5].InnerText, "\\s+(&nbsp;)*", "")
                                };
                                var aNode = tds[6].SelectSingleNode("./a");
                                if (aNode != null)
                                {
                                    var href = aNode.Attributes["href"] == null ? string.Empty : aNode.Attributes["href"].Value;
                                    if (!string.IsNullOrWhiteSpace(href))
                                    {
                                        _request.AddOrUpdateRequestParameter("guquandongjieDetailUrl", href);
                                        List<ResponseInfo> responseList = _request.GetResponseInfo(_requestXml.GetRequestListByName("guquandongjieDetail"));
                                        if (responseList != null && responseList.Any())
                                        {
                                            LoadAndParseGuQuanDongjieDetail(responseList[0].Data, jf);
                                        }
                                    }
                                }
                                _enterpriseInfo.judicial_freezes.Add(jf);
                            }
                        }
                    }
                }
            }

        }
        /// <summary>
        /// 股权冻结详情
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="jf"></param>
        void LoadAndParseGuQuanDongjieDetail(string responseData, JudicialFreeze jf)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            var div = rootNode.SelectSingleNode("//div[@class='detail-info']");
            if (div != null)
            {
                var table = div.SelectSingleNode("./table");
                if (table != null)
                {
                    var trs = table.SelectNodes("./tr");
                    if (trs != null && trs.Count > 1)
                    {
                        var first = trs.First();
                        var title = first.InnerText;
                        trs.Remove(first);
                        if (title.Contains("冻结情况"))
                        {
                            JudicialFreezeDetail detail = new JudicialFreezeDetail();
                            foreach (var tr in trs)
                            {
                                var ths = tr.SelectNodes("./th");
                                var tds = tr.SelectNodes("./td");
                                if (tds != null && ths != null && tds.Count == ths.Count)
                                {
                                    for (int i = 0; i <= ths.Count - 1; i++)
                                    {
                                        if (ths[i].InnerText.Equals("执行法院"))
                                        {
                                            detail.execute_court = Regex.Replace(tds[i].InnerText, "\\s+(&nbsp;)*", "");
                                        }
                                        else if (ths[i].InnerText.Equals("执行事项"))
                                        {
                                            detail.assist_item = Regex.Replace(tds[i].InnerText, "\\s+(&nbsp;)*", "");
                                        }
                                        else if (ths[i].InnerText.Equals("执行裁定书文号"))
                                        {
                                            detail.adjudicate_no = Regex.Replace(tds[i].InnerText, "\\s+(&nbsp;)*", "");
                                        }
                                        else if (ths[i].InnerText.Equals("执行通知书文号"))
                                        {
                                            detail.notice_no = Regex.Replace(tds[i].InnerText, "\\s+(&nbsp;)*", "");
                                        }
                                        else if (ths[i].InnerText.Equals("被执行人"))
                                        {
                                            detail.assist_name = Regex.Replace(tds[i].InnerText, "\\s+(&nbsp;)*", "");
                                        }
                                        else if (ths[i].InnerText.Equals("被执行人持有股权、其它投资权益的数额"))
                                        {
                                            detail.freeze_amount = Regex.Replace(tds[i].InnerText, "\\s+(&nbsp;)*", "");
                                        }
                                        else if (ths[i].InnerText.Equals("被执行人证件种类"))
                                        {
                                            detail.assist_ident_type = Regex.Replace(tds[i].InnerText, "\\s+(&nbsp;)*", "");
                                        }
                                        else if (ths[i].InnerText.Equals("被执行人证件号码"))
                                        {
                                            detail.assist_ident_no = Regex.Replace(tds[i].InnerText, "\\s+(&nbsp;)*", "");
                                        }
                                        else if (ths[i].InnerText.Equals("冻结期限自"))
                                        {
                                            var content = Regex.Replace(tds[i].InnerText, "\\s+(&nbsp;)*", "");
                                            var index = content.IndexOf("冻结期限至");
                                            if (index > -1)
                                            {
                                                detail.freeze_start_date = content.Substring(0, index);
                                                detail.freeze_end_date = content.Substring(index + 5);
                                            }
                                            else
                                            {
                                                detail.freeze_start_date = Regex.Replace(tds[i].InnerText, "\\s+(&nbsp;)*", "");
                                            }
                                            
                                            
                                        }
                                        else if (ths[i].InnerText.Equals("冻结期限至"))
                                        {
                                            detail.freeze_end_date = Regex.Replace(tds[i].InnerText, "\\s+(&nbsp;)*", "");
                                        }
                                        else if (ths[i].InnerText.Equals("冻结期限"))
                                        {
                                            detail.freeze_year_month = Regex.Replace(tds[i].InnerText, "\\s+(&nbsp;)*", "");
                                        }
                                        else if (ths[i].InnerText.Equals("公示日期"))
                                        {
                                            detail.public_date = Regex.Replace(tds[i].InnerText, "\\s+(&nbsp;)*", "");
                                        }
                                    }
                                }
                            }
                            jf.detail = detail;
                        }
                        else if (title.Contains("解冻情况"))
                        {
                            JudicialUnFreezeDetail unfreeze = new JudicialUnFreezeDetail();
                            foreach (var tr in trs)
                            {
                                for (int i = 0; i < trs.Count; i++)
                                {
                                    HtmlNodeCollection thList = trs[i].SelectNodes("./th");
                                    HtmlNodeCollection tdList = trs[i].SelectNodes("./td");
                                    if (thList != null && tdList != null && thList.Count == tdList.Count)
                                    {
                                        for (int j = 0; j < thList.Count; j++)
                                        {
                                            switch (thList[j].InnerText.Trim())
                                            {
                                                case "执行法院":
                                                    unfreeze.execute_court = tdList[j].InnerText.Trim();
                                                    break;
                                                case "执行事项":
                                                    unfreeze.assist_item = tdList[j].InnerText.Trim();
                                                    break;
                                                case "执行裁定书文号":
                                                    unfreeze.adjudicate_no = tdList[j].InnerText.Trim();
                                                    break;
                                                case "执行通知书文号":
                                                    unfreeze.notice_no = tdList[j].InnerText.Trim();
                                                    break;
                                                case "被执行人":
                                                    unfreeze.assist_name = tdList[j].InnerText.Trim();
                                                    break;
                                                case "被执行人持有股份、其他投资权益的数额":
                                                case "被执行人持有股权、其它投资权益的数额":
                                                    unfreeze.freeze_amount = tdList[j].InnerText.Trim();
                                                    break;
                                                case "被执行人证件种类":
                                                    unfreeze.assist_ident_type = tdList[j].InnerText.Trim();
                                                    break;
                                                case "被执行人证件号码":
                                                    unfreeze.assist_ident_no = tdList[j].InnerText.Trim();
                                                    break;
                                                case "解除冻结日期":

                                                    var content = Regex.Replace(tdList[j].InnerText, "\\s+(&nbsp;)*", "");
                                                    var index = content.IndexOf("公示日期");
                                                    if (index > -1)
                                                    {
                                                        unfreeze.unfreeze_date = content.Substring(0, index);
                                                        unfreeze.public_date = content.Substring(index + 4);
                                                    }
                                                    else
                                                    {
                                                        unfreeze.unfreeze_date = content;
                                                    }
                                                    break;
                                                case "公示日期":
                                                    unfreeze.public_date = Regex.Replace(tdList[j].InnerText, "\\s+(&nbsp;)*", "");
                                                    break;
                                            }
                                        }
                                    }
                                }
                            }
                            
                            jf.un_freeze_detail = unfreeze;
                        }
                    }
                }
            }
        }
    }
}