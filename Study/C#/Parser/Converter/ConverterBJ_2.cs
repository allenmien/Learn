using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using System.Reflection;
using System.Collections;
using System.Xml.Linq;

using Newtonsoft.Json;
using HtmlAgilityPack;
using iOubo.iSpider.Model;
using iOubo.iSpider.Common;
using System.Configuration;

namespace iOubo.iSpider.Infrastructure.Parser
{
    public class ConverterBJ2 : IConverter
    {

        DataRequest _request;
        RequestInfo _requestInfo;
        RequestXml _requestXml;
        EnterpriseInfo _enterpriseInfo = new EnterpriseInfo();
        List<AbnormalInfo> _abnormals = new List<AbnormalInfo>();
        List<CheckupInfo> _checkups = new List<CheckupInfo>();
        string _status = string.Empty;

        private string isParallelRequest = ConfigurationManager.AppSettings["IsParallelRequest"] == null ? "Y" : ConfigurationManager.AppSettings["IsParallelRequest"];
        private ResponseInfo globalResponseInfo = null;
        private bool isLoadPartner = true;
        private bool isIndiviual = false;
        List<string> reportsNeedToLoad = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings.Get("ReportsNeedToLoad")) 
            ? new List<string>() : ConfigurationManager.AppSettings.Get("ReportsNeedToLoad").Split(',').ToList();
        int maxParallelCount = int.Parse(ConfigurationManager.AppSettings["MaxParallelRequest"] == null ? "1" : ConfigurationManager.AppSettings["MaxParallelRequest"]);

        public SummaryEntity ProcessRequestAndParse(RequestInfo requestInfo)
        {
            requestInfo.Province = "BJ";

            this._requestInfo = requestInfo;
            this._request = new DataRequest(requestInfo);
            if (requestInfo.Parameters.ContainsKey("platform") && "LIST_API" == requestInfo.Parameters["platform"])
            {
                this._requestXml = new RequestXml(requestInfo.CurrentPath, "BJ1" + "_API");
            }
            else
            {
                this._requestXml = new RequestXml(requestInfo.CurrentPath, "BJ1");
            }
            InitialEnterpriseInfo();
            List<ResponseInfo> responseList=new List<ResponseInfo>();
            if (requestInfo.Parameters.ContainsKey("name") && !string.IsNullOrWhiteSpace(requestInfo.Parameters["name"]))
            {
                var tempName = requestInfo.Parameters["name"];
                tempName = tempName.Replace('(', '（').Replace(')', '）');
                if (tempName.Contains("（") && tempName.Contains("）"))
                {
                    var tempArr = tempName.Split(new char[] { '（', '）' });
                    _status = tempArr[tempArr.Length - 2];
                    _enterpriseInfo.status = _status;
                }
            }
            //数据请求和解析
            if (requestInfo.Parameters.ContainsKey("name") && requestInfo.Parameters["name"] != null && requestInfo.Parameters["name"].EndsWith("（个体转企业）"))
            {

                //var requestList = _requestXml.GetRequestListByGroup("basic").Where(p => !p.Attribute("name").Value.StartsWith("stockHolders"));
                //responseList = _request.GetResponseInfo(requestList);
            }
            else
            {
                try 
                {
                    responseList = _request.GetResponseInfo(_requestXml.GetRequestListByGroup("stockHolders"));
                    globalResponseInfo = responseList.FirstOrDefault(p => p.Name == "stockHolders2");
                }
                catch { }
            }
            responseList.AddRange(_request.GetResponseInfo(_requestXml.GetRequestListByGroup("basic")));
            if (isParallelRequest == "Y")
            {
                this.ParseResponseMainInfoParallel(responseList);
            }
            else
            {
                this.ParseResponseMainInfo(responseList);
            }
            
            SummaryEntity summaryEntity = new SummaryEntity();
            summaryEntity.Enterprise = _enterpriseInfo;
            summaryEntity.Abnormals = _abnormals;
            summaryEntity.Checkups = _checkups;
            if (summaryEntity.Enterprise.partners_hidden.Any())
            {
                summaryEntity.Enterprise.partner_hidden_flag = 1;
            }
            //如果使用注册号抓取工商信息时，没有抓取到注册号，则将搜索的注册号关键字付给reg_no
            if (!string.IsNullOrEmpty(summaryEntity.Enterprise.name) && string.IsNullOrEmpty(summaryEntity.Enterprise.reg_no) && !string.IsNullOrEmpty(requestInfo.RegNo) && DataHandler.isRegNo(requestInfo.RegNo))
            {
                summaryEntity.Enterprise.reg_no = requestInfo.RegNo;
            }

            return summaryEntity;
        }
        DataRequest CreateRequest()
        {
            Dictionary<string, string> dic = new Dictionary<string, string>();
            RequestInfo rInfo = new RequestInfo()
            {

                Cookies = _requestInfo.Cookies,
                Headers = _requestInfo.Headers,
                Province = "BJ",
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
            request.AddRequestParameterIfNotExist("now", DateTime.Now.ToString());
            return request;
        }

        private void InitialEnterpriseInfo()
        {
            _enterpriseInfo.province = "BJ";
            _enterpriseInfo.last_update_time = DateTime.Now;
            _enterpriseInfo.source = "Batch";
            if (this._requestInfo.Parameters.ContainsKey("platform"))
            {
                this._requestInfo.Parameters.Remove("platform");
            }
            _enterpriseInfo.parameters = this._requestInfo.Parameters;
        }

        private void ParseResponseMainInfoParallel(List<ResponseInfo> responseInfoList)
        {
            if (responseInfoList != null)
            {
                var basicInfo = responseInfoList.FirstOrDefault(p => p.Name == "basicInfo");
                if (basicInfo != null)
                {
                    this.LoadAndParseBasicInfo(basicInfo.Data, _enterpriseInfo);
                }
                Parallel.ForEach(responseInfoList, new ParallelOptions { MaxDegreeOfParallelism = maxParallelCount }, responseInfo => LoadData(responseInfo));
                Parallel.ForEach(responseInfoList, new ParallelOptions { MaxDegreeOfParallelism = maxParallelCount }, responseInfo => LoadOtherData(responseInfo));
                
            }
        }

        private void ParseResponseMainInfo(List<ResponseInfo> responseInfoList)
        {
            if (responseInfoList != null)
            {
                foreach (var responseInfo in responseInfoList)
                {
                    LoadData(responseInfo);
                    LoadOtherData(responseInfo);
                }
            }
        }

        void LoadData(ResponseInfo responseInfo)
        {
            if (responseInfo == null) { return; }
            switch (responseInfo.Name)
            {
                //case "basicInfo":
                //    LoadAndParseBasicInfo(responseInfo.Data, _enterpriseInfo);
                //    break;
                case "stockHolders":
                    LoadAndParsePartners(responseInfo.Data, _enterpriseInfo);
                    break;
                case "mainStaffs":
                    LoadAndParseStaffs(responseInfo.Data, _enterpriseInfo);
                    break;
                case "branches":
                    LoadAndParseBranches(responseInfo.Data, _enterpriseInfo);
                    break;

            }

        }

        private 



        void LoadOtherData(ResponseInfo responseInfo)
        {
            switch (responseInfo.Name)
            {
                case "changeLogs":
                    LoadAndParseChangeRecords(responseInfo.Data, _enterpriseInfo);
                    break;
                case "gudongjichuzi":
                    LoadAndParseGuDongJiChuZi(responseInfo.Data, _enterpriseInfo);
                    break;
                case "guquanbiangeng":
                    LoadAndParseGuQuanBianGeng(responseInfo.Data, _enterpriseInfo);
                    break;
                case "xingzhengxuke":
                    LoadAndParseXingZhengXuKe(responseInfo.Data, _enterpriseInfo);
                    break;
                case "reportItems":
                    LoadAndParseReports(responseInfo.Data, _enterpriseInfo);
                    if (isParallelRequest == "Y")
                    {
                        Parallel.ForEach(_enterpriseInfo.reports, new ParallelOptions { MaxDegreeOfParallelism = maxParallelCount }, report => GetReports(report));
                    }
                    else
                    {
                        foreach (var report in _enterpriseInfo.reports)
                        {
                            GetReports(report);
                        }
                    }
                    break;
                case "prompt":
                    LoadAndParsePrompt(responseInfo.Data, _enterpriseInfo);
                    break;
                case "remindmsg":
                    LoadAndParseXingZhengChuFa(responseInfo.Data, _enterpriseInfo);
                    break;
                case "jingyingyichang":
                    LoadAndParseJingYingYiChang(responseInfo.Data, _enterpriseInfo);
                    break;
                case "zhishichanquan":
                    LoadAndParseZhiShiChanQuan(responseInfo.Data, _enterpriseInfo);
                    break;
                default:
                    break;
            }

        }
        void GetReports(Report report)
        {
            var request = CreateRequest();
            request.AddOrUpdateRequestParameter("reportId", report.ex_id);
            request.AddOrUpdateRequestParameter("report_stockInfo_pageNos", "1");
            request.AddOrUpdateRequestParameter("report_investInfo_pageNos", "1");
            request.AddOrUpdateRequestParameter("report_externalGuaranteeInfo_pageNos", "1");
            request.AddOrUpdateRequestParameter("report_updateRecordsInfo_pageNos", "1");
            request.AddOrUpdateRequestParameter("report_guquanbiangeng_pageNos", "1");
            request.AddOrUpdateRequestParameter("report_updateRecordsInfo_year", report.report_year);
            var response = isIndiviual ? request.GetResponseInfo(_requestXml.GetRequestListByGroup("reportIndiviualDetail"))
                : request.GetResponseInfo(_requestXml.GetRequestListByGroup("report"));
            ParseReportInfo(response, report);
            
        }
        private void ParseReportInfo(List<ResponseInfo> responseInfoList, Report report)
        {
            if (isParallelRequest == "Y")
            {
                Parallel.ForEach(responseInfoList, new ParallelOptions() { MaxDegreeOfParallelism = maxParallelCount }, responseInfo => LoadReport(responseInfo, report));
            }
            else
            {
                foreach (var responseInfo in responseInfoList)
                {
                    LoadReport(responseInfo, report);
                }
            }
        }
        void LoadReport(ResponseInfo responseInfo, Report report)
        {
            switch (responseInfo.Name)
            {
                case "report_basic":
                    LoadAndParseReportBasic(responseInfo.Data, report);
                    break;
                case "report_website":
                    LoadAndParseReportWebsite(responseInfo.Data, report);
                    break;
                case "report_stockInfo":
                    LoadAndParseReportPartner(responseInfo.Data, report);
                    break;
                case "report_investInfo":
                    LoadAndParseReportInvest(responseInfo.Data, report);
                    break;
                case "report_externalGuaranteeInfo":
                    LoadAndParseReportExternalGuarantee(responseInfo.Data, report);
                    break;
                case "report_updateRecordsInfo":
                    LoadAndParseReportUpdateRecords(responseInfo.Data, report);
                    break;
                case "report_guquanbiangengInfo":
                    LoadAndParseReportGuQuanBianGeng(responseInfo.Data, report);
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// 解析基本信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseBasicInfo(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            //LogHelper.Info(responseData);
            try
            {
                IsIndiviual(responseData);
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;
                var partnerNode = rootNode.SelectSingleNode("//div[@id='tzrlistThree']");
                if (partnerNode != null)
                {
                    isLoadPartner = partnerNode.OuterHtml.Contains("showDialog('/xycx/queryCreditAction!tzrlist_all.dhtml?reg_bus_ent_id=") ? true : false;
                }
                else
                {
                    isLoadPartner = false;
                }
                HtmlNodeCollection tables = rootNode.SelectNodes("//table[@class='f-lbiao']");

                if (tables != null)
                {
                    Address addressJingYin = new Address();

                    foreach (HtmlNode table in tables)
                    {
                        HtmlNodeCollection trList = table.SelectNodes("./tr");
                        if (!table.InnerText.Contains("纳税人名称"))
                        {
                            foreach (HtmlNode tr in trList)
                            {
                                HtmlNodeCollection tdList = tr.SelectNodes("./td");
                                if (tdList != null && tdList.Count % 2 == 0)
                                {
                                    for (int i = 0; i < tdList.Count; i += 2)
                                    {
                                        var lblName = Regex.Replace(tdList[i].InnerText, @"(\s+)|(&nbsp;)+", "");
                                        var lblValue = Regex.Replace(tdList[i + 1].InnerText, @"(\s+)|(&nbsp;)+|(null)+", "", RegexOptions.IgnoreCase);
                                        switch (lblName)
                                        {
                                            case "注册号：":
                                                _enterpriseInfo.reg_no = lblValue;
                                                break;
                                            case "统一社会信用代码：":
                                                _enterpriseInfo.credit_no = lblValue;
                                                break;
                                            case "注册号/统一社会信用代码：":
                                            case "统一社会信用代码/注册号：":
                                                if (lblValue.Length == 18)
                                                    _enterpriseInfo.credit_no = lblValue;
                                                else
                                                    _enterpriseInfo.reg_no = lblValue;
                                                break;
                                            case "名称：":
                                            case "企业名称：":
                                                _enterpriseInfo.name = lblValue.Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                                                break;
                                            case "类型：":
                                            case "公司类型：":
                                            case "经济性质：":
                                            case "组成形式":
                                            case "组成形式 ：":
                                            case "组成形式：":
                                                _enterpriseInfo.econ_kind = lblValue;
                                                break;
                                            case "法定代表人：":
                                            case "负责人：":
                                            case "股东：":
                                            case "经营者：":
                                            case "执行事务合伙人：":
                                            case "投资人：":
                                            case "经营者姓名：":
                                            case "投资人姓名：":
                                                _enterpriseInfo.oper_name = lblValue;
                                                break;
                                            case "住所：":
                                            case "主要经营场所：":
                                            case "营业场所：":
                                            case "企业住所：":
                                                Address address = new Address();
                                                address.name = "注册地址";
                                                address.address = lblValue;
                                                address.postcode = "";
                                                _enterpriseInfo.addresses.Add(address);
                                                break;
                                            case "注册资本：":
                                            case "注册资金：":
                                                _enterpriseInfo.regist_capi = lblValue;
                                                break;
                                            case "成立日期：":
                                            case "登记日期：":
                                            case "注册日期：":
                                                _enterpriseInfo.start_date = lblValue;
                                                break;

                                            case "营业期限自：":
                                            case "经营期限自：":
                                            case "合伙期限自：":
                                                _enterpriseInfo.term_start = lblValue;
                                                break;
                                            case "营业期限至：":
                                            case "经营期限至：":
                                            case "合伙期限至：":
                                                _enterpriseInfo.term_end = lblValue;
                                                break;
                                            case "经营范围：":
                                            case "许可经营项目：":
                                            case "一般经营项目：":
                                            case "业务范围：":
                                                _enterpriseInfo.scope += lblValue;
                                                break;
                                            case "登记机关：":
                                            case "发照机关：":
                                                _enterpriseInfo.belong_org = lblValue;
                                                break;
                                            case "发照日期：":
                                            case "核准日期：":
                                                _enterpriseInfo.check_date = lblValue;
                                                break;
                                            case "登记状态：":
                                            case "企业状态：":
                                            case "企业状态 ：":
                                            case "状态：":
                                                _enterpriseInfo.status = lblValue;
                                                break;
                                            case "吊销日期：":
                                            case "注销日期：":
                                                _enterpriseInfo.end_date = lblValue;
                                                break;
                                            case "实收资本：":
                                                _enterpriseInfo.actual_capi = lblValue;
                                                break;
                                            case "组织机构代码：":
                                                _enterpriseInfo.org_no = lblValue;
                                                break;
                                            case "税务登记证号：":
                                                _enterpriseInfo.tax_no = lblValue;
                                                break;
                                            case "经营地址：":
                                                addressJingYin.name = "经营地址";
                                                addressJingYin.address = lblValue;
                                                _enterpriseInfo.addresses.Insert(0, addressJingYin);
                                                break;
                                            case "经营地址邮编：":
                                                addressJingYin.postcode = lblValue;
                                                break;
                                            default:
                                                break;
                                        }
                                    }
                                }
                                else
                                {
                                    //注：该企业原营业执照记载的注册号为110108014181972，现已换发了加载统一社会信用代码的营业执照。
                                    var lblTxt = tr.InnerText;
                                    if (lblTxt.StartsWith("注：该企业原营业执照记载的注册号为"))
                                    {
                                        lblTxt = lblTxt.Replace("注：该企业原营业执照记载的注册号为", "");
                                        var startIndex = lblTxt.IndexOf('，');
                                        _enterpriseInfo.reg_no = lblTxt.Substring(0, startIndex);
                                    }
                                }
                            }
                        }
                        else
                        {// 税务登记信息
                            foreach (HtmlNode tr in trList)
                            {
                                HtmlNodeCollection thList = tr.SelectNodes("./th");
                                HtmlNodeCollection tdList = tr.SelectNodes("./td");
                                if (thList != null && tdList != null && thList.Count == tdList.Count)
                                {
                                    for (int i = 0; i < thList.Count; i++)
                                    {
                                        var lblName = Regex.Replace(thList[i].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                                        var lblValue = Regex.Replace(tdList[i].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);

                                        switch (lblName)
                                        {
                                            case "税务登记证号：":
                                                _enterpriseInfo.tax_no = lblValue;
                                                break;
                                            case "经营地址：":
                                                addressJingYin.name = "经营地址";
                                                addressJingYin.address = lblValue;
                                                _enterpriseInfo.addresses.Insert(0, addressJingYin);
                                                break;
                                            case "经营地址邮编：":
                                                addressJingYin.postcode = lblValue;
                                                break;
                                            case "登记注册类型：":
                                                if (string.IsNullOrWhiteSpace(_enterpriseInfo.econ_kind))
                                                {
                                                    _enterpriseInfo.econ_kind = lblValue;
                                                }
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
                else
                {
                    if (_requestInfo.Parameters.ContainsKey("name"))
                    {
                        _enterpriseInfo.name = _requestInfo.Parameters["name"];
                    }
                }
            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                LogHelper.Error("解析数据出错", ex);
                throw;
            }
        }

        private void IsIndiviual(string response)
        {
            isIndiviual = response.Contains("entGSview('gtnbDiv'") ? true : false;
        }

        private void CheckResponseAndLog(string responseData)
        {
            LogHelper.Info("Server Error, length=" + responseData.Length);
            LogHelper.Info("Server Error, responseData=" + responseData);
            if (responseData.Length == 874 || responseData.Length == 908)
            {
                Console.WriteLine("Server Error, sleep 60s...");
                LogHelper.Info("Server Error, sleep 60s...");
                Thread.Sleep(60000);
            }

        }

        /// <summary>
        /// 股东
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParsePartners(string responseData, EnterpriseInfo _enterpriseInfo,int count=1)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(responseData))
                {
                    return;
                }
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;
                HtmlNode infoTable = rootNode.SelectSingleNode("//table[@id='tableIdStyle']");
                HtmlNodeCollection trList = infoTable.SelectNodes("./tr");

                if (trList != null && trList.Count > 2)
                {
                    LoadAndParsePartnersData(trList);
                }
                else
                {
                    if (globalResponseInfo != null)
                    {
                        HtmlDocument document1 = new HtmlDocument();
                        document1.LoadHtml(globalResponseInfo.Data);
                        HtmlNode rootNode1 = document1.DocumentNode;
                        HtmlNode infoTable1 = rootNode1.SelectSingleNode("//table[@id='tableIdStyle']");
                        HtmlNodeCollection trList1 = infoTable1.SelectNodes("./tr");
                        if (trList1 != null)
                        {
                            LoadAndParsePartnersData(trList1);
                            this.CheckPartnerDetail(_enterpriseInfo.partners);
                        }
                    }

                }
                if (!_enterpriseInfo.partners.Any() &&!_enterpriseInfo.partners_hidden.Any())
                {
                    //count < 10 && !_requestInfo.Parameters["name"].EndsWith("（个体转企业）")
                    if (count < 10 && isLoadPartner)
                    {
                        LogHelper.Info(string.Format("公司名称：【{0}】，responseData：【{1}】", _enterpriseInfo.name, responseData));
                        count++;
                        var requestList2 = _requestXml.GetRequestListByName("stockHolder");
                        var responseList2 = _request.GetResponseInfo(requestList2);
                        if (requestList2 != null && requestList2.Any())
                        {
                            LoadAndParsePartners(responseList2[0].Data, _enterpriseInfo, count);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                throw;
            }
        }

        void LoadAndParsePartnersData(HtmlNodeCollection trList)
        {
            List<Partner> partnerList = new List<Partner>();
            foreach (HtmlNode rowNode in trList)
            {
                HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                if (tdList != null && tdList.Count > 4)
                {
                    Partner partner = new Partner();
                    partner.seq_no = int.Parse(Regex.Replace(tdList[0].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase));
                    partner.stock_name = Regex.Replace(tdList[1].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                    partner.stock_type = Regex.Replace(tdList[2].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                    partner.identify_type = Regex.Replace(tdList[3].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                    partner.identify_no = Regex.Replace(tdList[4].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                    ShouldAndRealCapiItem sarCapiItem = new ShouldAndRealCapiItem();
                    if (tdList.Count >= 9)
                    {
                        ShouldCapiItem sci = new ShouldCapiItem();
                        RealCapiItem rci = new RealCapiItem();
                        var shoud_capi = Regex.Replace(tdList[5].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                        sci.shoud_capi = shoud_capi;
                        sci.invest_type = Regex.Replace(tdList[6].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                        sci.should_capi_date = "";
                        
                        rci.real_capi = Regex.Replace(tdList[7].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                        rci.invest_type = Regex.Replace(tdList[8].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                        rci.real_capi_date = "";

                        partner.should_capi_items.Add(sci);
                        if (!string.IsNullOrWhiteSpace(rci.real_capi) && rci.real_capi != "0")
                        {
                            partner.real_capi_items.Add(rci);
                        }
                        
                        sarCapiItem.should_capi_items.Add(sci);
                        sarCapiItem.real_capi_items.Add(rci);
                        
                    }

                    partner.stock_percent = "";
                    partner.ex_id = JsonConvert.SerializeObject(sarCapiItem);
                    if (string.IsNullOrWhiteSpace(partner.stock_name) && string.IsNullOrWhiteSpace(partner.stock_type))
                    {
                        continue;
                    }
                    else
                    {
                        partnerList.Add(partner);
                    }
                }
            }
            foreach(var partner in partnerList)
            {
               if(partner.should_capi_items!=null)
               {
                   double total = 0;
                   foreach(var item in partner.should_capi_items)
                   {
                       total += Utility.GetNumber(item.shoud_capi).HasValue ? Utility.GetNumber(item.shoud_capi).Value : 0;
                   }
                   partner.total_should_capi = total == 0 ? string.Empty : total.ToString();
               }

               if (partner.real_capi_items != null)
               {
                   double total = 0;
                   foreach (var item in partner.real_capi_items)
                   {
                       total += Utility.GetNumber(item.real_capi).HasValue ? Utility.GetNumber(item.real_capi).Value : 0;
                   }
                   partner.total_real_capi = total == 0 ? string.Empty : total.ToString();
               }
            }
            if (isLoadPartner)
            {
                _enterpriseInfo.partners = partnerList;
            }
            else
            {
                _enterpriseInfo.partners_hidden = partnerList;
            }
        }
        /// <summary>
        /// 主要人员
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseStaffs(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            try
            {
                List<Employee> employeeList = new List<Employee>();

                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;
                HtmlNode infoTable = rootNode.SelectSingleNode("//table[@id='tableIdStyle']");
                HtmlNodeCollection trList = infoTable.SelectNodes("./tr");

                if (trList != null)
                {
                    int seq = 1;
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                        if (tdList != null && tdList.Count > 3)
                        {
                            Employee employee = new Employee();
                            employee.seq_no = seq++;
                            employee.name = Regex.Replace(tdList[1].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                            employee.job_title = Regex.Replace(tdList[2].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                            employee.sex = Regex.Replace(tdList[3].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                            employee.cer_no = "";

                            employeeList.Add(employee);
                        }
                    }
                }

                _enterpriseInfo.employees = employeeList;
            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                throw;
            }
        }

        /// <summary>
        /// 变更
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseChangeRecords(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            try
            {

                var request = CreateRequest();
                List<ChangeRecord> changeRecordList = new List<ChangeRecord>();

                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;
                HtmlNode infoTable = rootNode.SelectSingleNode("//table[@id='tableIdStyle']");
                HtmlNodeCollection trList = infoTable.SelectNodes("./tr");

                int seq = 1;
                foreach (HtmlNode rowNode in trList)
                {
                    HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                    if (tdList != null && tdList.Count > 3)
                    {
                        ChangeRecord changeRecord = new ChangeRecord();
                        changeRecord.change_item = tdList[2].InnerText.Trim();
                        changeRecord.change_date = tdList[1].InnerText.Trim();
                        changeRecord.seq_no = seq++;

                        // 详情：有两种情况
                        List<ResponseInfo> responseList = new List<ResponseInfo>();
                        int category = 1;
                        string href = tdList[3].SelectSingleNode("./a").Attributes["onclick"].Value;
                        var detailUrl = href.Split('\'')[1];
                        request.AddOrUpdateRequestParameter("changeLogsDetailUrl", detailUrl);
                        responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("changeLogsDetail"));
                        if (string.IsNullOrWhiteSpace(responseList[0].Data))
                        {
                            continue;
                        }
                        category = detailUrl.Contains("old_reg_his_id") ? 2 : 1;
                        //string chr_id = Regex.Split(Regex.Split(href, "chr_id=")[1], "'")[0];
                        //request.AddOrUpdateRequestParameter("chr_id", chr_id);
                        //if (href.Contains("old_reg_his_id"))
                        //{
                        //    int startIndex = responseData.IndexOf("old_reg_his_id=");
                        //    int endIndex = responseData.IndexOf("&", startIndex);
                        //    string old_reg_his_id = responseData.Substring(startIndex + "old_reg_his_id=".Length, endIndex - startIndex - "old_reg_his_id=".Length);
                        //    startIndex = responseData.IndexOf("new_reg_his_id=");
                        //    endIndex = responseData.IndexOf("&", startIndex);
                        //    string new_reg_his_id = responseData.Substring(startIndex + "new_reg_his_id=".Length, endIndex - startIndex - "new_reg_his_id=".Length);
                        //    request.AddOrUpdateRequestParameter("old_reg_his_id", old_reg_his_id);
                        //    request.AddOrUpdateRequestParameter("new_reg_his_id", new_reg_his_id);
                        //    responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("changeLogsDetail2"));
                        //    category = 2;
                        //}
                        //else
                        //{
                        //    responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("changeLogsDetail1"));
                        //}

                        if (responseList != null && responseList.Count > 0)
                        {
                            HtmlDocument documentDetail = new HtmlDocument();
                            documentDetail.LoadHtml(responseList[0].Data);
                            HtmlNode rootNodeDetail = documentDetail.DocumentNode;
                            if (changeRecord.change_item.Contains("董事") || changeRecord.change_item.Contains("理事") || changeRecord.change_item.Contains("经理") || changeRecord.change_item.Contains("监事"))
                            {
                                changeRecord.before_content = "（注：标有*标志的为法定代表人）\r\n";
                                changeRecord.after_content = "（注：标有*标志的为法定代表人）\r\n";
                            }
                            if (category == 2)
                            {
                                HtmlNodeCollection tables = rootNodeDetail.SelectNodes("//table[@id='tableIdStyle']");
                                if (tables == null)
                                {
                                    tables = rootNodeDetail.SelectNodes("//table[@class='tableIdStyle']");
                                }
                                HtmlNodeCollection detailTrList = tables[0].SelectNodes("./tr");
                                for (int i = 2; i < detailTrList.Count; i++)
                                {
                                    HtmlNodeCollection detailTdList = detailTrList[i].SelectNodes("./td");
                                    foreach (HtmlNode td in detailTdList)
                                    {
                                        changeRecord.before_content += td.InnerText.Trim() + " ";
                                    }
                                    changeRecord.before_content += "\r\n";
                                }
                                detailTrList = tables[1].SelectNodes("./tr");
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
                            else if (category == 1)
                            {
                                HtmlNodeCollection tables = rootNodeDetail.SelectNodes("//table[@class='tableIdStyle']");
                                changeRecord.before_content = tables[1].SelectNodes("./tr/td")[0].InnerText.Trim();
                                changeRecord.after_content = tables[1].SelectNodes("./tr/td")[1].InnerText.Trim();
                            }

                        }
                        changeRecordList.Add(changeRecord);
                    }
                }
                _enterpriseInfo.changerecords = changeRecordList;
            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                CheckResponseAndLog(ex.StackTrace);
                throw;
            }
        }

        /// <summary>
        /// 分支机构
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseBranches(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            try
            {
                List<Branch> branchList = new List<Branch>();

                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;
                HtmlNode infoTable = rootNode.SelectSingleNode("//table[@id='tableIdStyle']");

                int seq = 1;
                HtmlNodeCollection trList = infoTable.SelectNodes("./tr");
                if (trList != null)
                {
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                        if (tdList != null && tdList.Count > 3)
                        {
                            Branch branch = new Branch();
                            branch.seq_no = seq++;
                            branch.belong_org = Regex.Replace(tdList[5].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                            branch.name = Regex.Replace(tdList[1].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                            branch.oper_name = Regex.Replace(tdList[3].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                            branch.reg_no = Regex.Replace(tdList[2].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);

                            branchList.Add(branch);
                        }
                    }
                }

                _enterpriseInfo.branches = branchList;
            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                throw;
            }
        }

        /// <summary>
        /// 提示信息：抽查检查、动产抵押、股权出质
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParsePrompt(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            try
            {
                var request = CreateRequest();
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;
                HtmlNode infoTable = rootNode.SelectSingleNode("//table");
                HtmlNodeCollection trList = infoTable.SelectNodes("./tr");
                if (trList != null)
                {
                    foreach (HtmlNode tr in trList)
                    {
                        HtmlNodeCollection tdList = tr.SelectNodes("./td");
                        for (int i = 0; i < tdList.Count; i++)
                        {
                            HtmlNode td = tdList[i];
                           if (td.InnerText.Contains("抽查信息"))
                            {
                                string onclick = td.SelectSingleNode("./a").Attributes["onclick"].Value;
                                string checkId = Regex.Split(onclick, "'")[5];
                                request.AddOrUpdateRequestParameter("checkId", checkId);
                                List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("check"));
                                if (responseList != null && responseList.Count > 0)
                                {
                                    
                                    //item.department = tdList[i + 1].InnerText.Trim();
                                    LoadAndParseCheckUpItems(responseList[0].Data, _enterpriseInfo);
                                }
                            }
                            //股权质押登记
                            else if (td.InnerText.Contains("股权质押登记信息"))
                            {
                                string onclick = td.SelectSingleNode("./a").Attributes["onclick"].Value;
                                string checkId = Regex.Split(onclick, "'")[5];
                                request.AddOrUpdateRequestParameter("guquanzhiyaId", checkId);
                                List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("guquanzhiya"));
                                if (responseList != null && responseList.Count > 0)
                                {
                                    LoadAndParseGuQuanZhiYa(responseList[0].Data, _enterpriseInfo);
                                }
                            }
                           else if (td.InnerText.Contains("动产抵押登记信息"))
                           {
                               string onclick = td.SelectSingleNode("./a").Attributes["onclick"].Value;
                               string checkId = Regex.Split(onclick, "'")[5];
                               request.AddOrUpdateRequestParameter("dongchandiyaId", checkId);
                               List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("dongchandiya"));
                               if (responseList != null && responseList.Count > 0)
                               {
                                   LoadAndParseDongChanDiYa(responseList[0].Data, _enterpriseInfo);
                               }
                               
                           }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                throw;
            }
        }
        void LoadAndParseJingYingYiChang(string responseData, EnterpriseInfo _enterpriseInfo)
        { 
        //jingyingyichang
            try
            {
                var request = CreateRequest();
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;
                HtmlNodeCollection tables = rootNode.SelectNodes("//table");
                if(tables!=null)
                {

                    foreach (var infoTable in tables)
                    {
                        HtmlNodeCollection trList = infoTable.SelectNodes("./tr");
                        if (trList != null)
                        {
                            foreach (HtmlNode tr in trList)
                            {
                                HtmlNodeCollection tdList = tr.SelectNodes("./td");
                                for (int i = 0; i < tdList.Count; i++)
                                {
                                    HtmlNode td = tdList[i];
                                    if (td.InnerText.Contains("经营异常名录"))
                                    {
                                        string onclick = td.SelectSingleNode("./a").Attributes["onclick"].Value;
                                        string jingyinId = Regex.Split(onclick, "'")[5];
                                        request.AddOrUpdateRequestParameter("jingyinId", jingyinId);
                                        List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("jingyin"));
                                        if (responseList != null && responseList.Count > 0)
                                        {
                                            LoadAndParseAbnormalItems(responseList[0].Data, _enterpriseInfo);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                throw;
            }
        }
        /// <summary>
        /// 行政处罚
        /// </summary>
        /// <param name="responseInfo"></param>
        private void LoadAndParseXingZhengChuFa(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            try
            {
                var request = CreateRequest();
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;
                HtmlNode infoTable = rootNode.SelectSingleNode("//table");
                HtmlNodeCollection trList = infoTable.SelectNodes("./tr");
                if (trList != null)
                {
                    Thread.Sleep(80);
                    foreach (HtmlNode tr in trList)
                    {
                        HtmlNodeCollection tdList = tr.SelectNodes("./td");
                        for (int i = 0; i < tdList.Count; i++)
                        {
                            HtmlNode td = tdList[i];
                            if (td.InnerText.Contains("行政处罚信息"))
                            {
                                string onclick = td.SelectSingleNode("./a").Attributes["onclick"].Value;
                                string xingzhengchufaId = Regex.Split(onclick, "'")[5];
                                request.AddOrUpdateRequestParameter("xingzhengchufaId", xingzhengchufaId);
                                List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("xingzhengchufa"));
                                if (responseList != null && responseList.Count > 0)
                                {
                                    LoadAndParseXingZhengChuFaItems(responseList[0].Data, _enterpriseInfo);
                                }
                            }
                            else if (td.InnerText.Contains("行政处罚"))
                            {
                                string onclick = td.SelectSingleNode("./a").Attributes["onclick"].Value;
                                string xingzhengchufaId = Regex.Split(onclick, "'")[5];
                                request.AddOrUpdateRequestParameter("xingzhengchufaId", xingzhengchufaId);
                                List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("xingzhengchufa_new"));
                                if (responseList != null && responseList.Count > 0)
                                {
                                    LoadAndParseXingZhengChuFaItems2(responseList[0].Data, _enterpriseInfo);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                throw;
            }
        }
        /// <summary>
        /// 行政处罚信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseXingZhengChuFaItems(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            responseData = responseData.Replace("<tr>", "").Replace("</tr>", "");
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var tables = rootNode.SelectNodes("//table");
            if (tables == null) return;
            Console.WriteLine(string.Format("行政处罚信息【{0}】条：{1}", tables.Count, _enterpriseInfo.name));
            LogHelper.Info(string.Format("行政处罚信息【{0}】条：{1}", tables.Count, _enterpriseInfo.name));
            foreach (var table in tables)
            {
                
                var tds = table.SelectNodes("./td");

                if (tds != null && tds.Count % 2 == 0)
                {
                    AdministrativePunishment item = new AdministrativePunishment();
                    item.seq_no = _enterpriseInfo.administrative_punishments.Count + 1;
                    item.number = Regex.Replace(tds[1].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                    item.name = Regex.Replace(tds[3].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                    item.reg_no =Regex.Replace( tds[5].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                    item.oper_name = Regex.Replace(tds[7].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                    item.illegal_type = Regex.Replace(tds[9].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                    item.department = Regex.Replace(tds[11].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                    item.content = Regex.Replace(tds[13].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                    item.date = Regex.Replace(tds[15].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                    item.remark = Regex.Replace(tds[17].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                    item.description = "";
                    item.based_on = "";
                    _enterpriseInfo.administrative_punishments.Add(item);
                }
            }
        }
        /// <summary>
        /// 行政处罚
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseXingZhengChuFaItems2(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            responseData = responseData.Replace("<tr>", "").Replace("</tr>", "");
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var tables = rootNode.SelectNodes("//table");
            if (tables == null) return;
            Console.WriteLine(string.Format("行政处罚【{0}】条：{1}", tables.Count, _enterpriseInfo.name));
            LogHelper.Info(string.Format("行政处罚【{0}】条：{1}", tables.Count, _enterpriseInfo.name));
            foreach (var table in tables)
            {

                var tds = table.SelectNodes("./td");

                if (tds != null && tds.Count % 2 == 0)
                {
                    AdministrativePunishment item = new AdministrativePunishment();
                    item.seq_no = _enterpriseInfo.administrative_punishments.Count + 1;
                    item.number = Regex.Replace(tds[5].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                    item.name = Regex.Replace(tds[1].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                    item.reg_no = string.IsNullOrWhiteSpace(_enterpriseInfo.reg_no) ? _enterpriseInfo.credit_no : _enterpriseInfo.reg_no;
                    item.oper_name = _enterpriseInfo.oper_name;
                    item.illegal_type = Regex.Replace(tds[7].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                    item.department = Regex.Replace(tds[11].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                    item.content = Regex.Replace(tds[9].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                    item.date = Regex.Replace(tds[13].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                    item.remark = Regex.Replace(tds[15].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                    item.description = "";
                    item.based_on = "";
                    _enterpriseInfo.administrative_punishments .Add(item);
                }
            }                                      
        }
        /// <summary>
        /// 经营异常信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseAbnormalItems(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            try
            {
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;
                HtmlNodeCollection tables = rootNode.SelectNodes("//table");
                if (tables != null)
                {
                    Console.WriteLine(string.Format("经营异常信息【{0}】条：{1}", tables.Count, _enterpriseInfo.name));
                    LogHelper.Info(string.Format("经营异常信息【{0}】条：{1}", tables.Count, _enterpriseInfo.name));
                        
                    foreach (var infoTable in tables)
                    {
                       
                        HtmlNodeCollection tdList = infoTable.SelectNodes("./td");
                        AbnormalInfo item = new AbnormalInfo();
                        item.name = _enterpriseInfo.name;
                        item.reg_no = _enterpriseInfo.reg_no;
                        item.province = _enterpriseInfo.province;
                        

                        if (tdList != null)
                        {
                            for (int i = 0; i < tdList.Count; i += 2)
                            {
                                var lblName = Regex.Replace(tdList[i].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                                var lblValue = Regex.Replace(tdList[i + 1].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                                switch (lblName)
                                {
                                    case "列入原因:":
                                        item.in_reason = lblValue;
                                        break;
                                    case "列入日期:":
                                        item.in_date = lblValue;
                                        break;
                                    case "移出原因:":
                                        item.out_reason = lblValue;
                                        break;
                                    case "移出日期:":
                                        item.out_date = lblValue;
                                        break;
                                    case "作出决定机关:":
                                    case "作出决定机关(列入):":
                                        item.department = lblValue;
                                        break;
                                }
                            }
                        }
                        this._abnormals.Add(item);
                    }
                }
                
            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                throw;
            }
        }

        /// <summary>
        /// 检查信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseCheckUpItems(string responseData,  EnterpriseInfo _enterpriseInfo)
        {
            try
            {
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;
                HtmlNodeCollection tables = rootNode.SelectNodes("//table");
                if(tables!=null)
                {
                    Console.WriteLine(string.Format("抽查检查信息【{0}】条：{1}", tables.Count, _enterpriseInfo.name));
                    LogHelper.Info(string.Format("抽查检查信息【{0}】条：{1}", tables.Count, _enterpriseInfo.name));
                    foreach (var infoTable in tables)
                    {
                        CheckupInfo item = new CheckupInfo();
                        item.name = _enterpriseInfo.name;
                        item.reg_no = _enterpriseInfo.reg_no;
                        item.province = _enterpriseInfo.province;

                        HtmlNodeCollection tdList = infoTable.SelectNodes("//td");
                        if (tdList != null)
                        {
                            if (tdList.Count == 12)
                            {
                                item.date = Regex.Replace(tdList[1].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                                item.department = Regex.Replace(tdList[5].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                                item.result = Regex.Replace(tdList[9].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                                _checkups.Add(item);
                            }
                            else if (tdList.Count == 6)
                            {
                                item.date = Regex.Replace(tdList[1].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                                item.department = Regex.Replace(tdList[3].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                                item.result = Regex.Replace(tdList[5].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                                _checkups.Add(item);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                throw;
            }
        }

        /// <summary>
        /// 股权质押登记
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseGuQuanZhiYa(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            try
            {
                responseData = responseData.Replace("<tr>", "").Replace("</tr>", "");
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;
                var tables = rootNode.SelectNodes("//table");
                List<EquityQuality> list = new List<EquityQuality>();
                if (tables != null)
                {
                    Console.WriteLine(string.Format("股权质押登记【{0}】条：{1}", tables.Count,_enterpriseInfo.name));
                    LogHelper.Info(string.Format("股权质押登记【{0}】条：{1}", tables.Count, _enterpriseInfo.name));
                    var c = 1;
                    foreach (HtmlNode table in tables)
                    {
                        
                        EquityQuality item = new EquityQuality();
                        var tds = table.SelectNodes("./td");
                        if (tds != null && tds.Count % 2 == 0)
                        {
                            item.seq_no = c;
                            item.number =  Regex.Replace(tds[1].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                            item.pledgor =  Regex.Replace(tds[3].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                            item.pledgor_identify_no =  Regex.Replace(tds[5].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);

                            item.pawnee =  Regex.Replace(tds[7].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                            item.pawnee_identify_no =  Regex.Replace(tds[9].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                            item.pledgor_amount = Regex.Replace( tds[11].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                            item.date =  Regex.Replace(tds[13].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                            item.status = Regex.Replace(tds[15].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);

                            list.Add(item);
                            c++;
                        }
                    }
                    _enterpriseInfo.equity_qualities = list;
                }
            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                throw;
            }
        }
        /// <summary>
        /// 动产抵押登记信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        void LoadAndParseDongChanDiYa(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            try
            {
                responseData = responseData.Replace("<tr>","").Replace("</tr>","");
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;
                var tables = rootNode.SelectNodes("//table");
                if (tables != null)
                {
                    Console.WriteLine(string.Format("动产抵押登记信息【{0}】条：{1}", tables.Count,_enterpriseInfo.name));
                    LogHelper.Info(string.Format("动产抵押登记信息【{0}】条：{1}", tables.Count, _enterpriseInfo.name));
                    var c = 1;
                    foreach (HtmlNode table in tables)
                    {
                        
                        MortgageInfo item = new MortgageInfo();
                        var tds = table.SelectNodes("./td");
                        if (tds != null && tds.Count % 2 == 0)
                        {
                            item.seq_no = _enterpriseInfo.mortgages.Count + 1;
                            item.number = Regex.Replace(tds[1].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                            item.date = Regex.Replace(tds[3].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                            item.department = Regex.Replace(tds[5].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);

                            item.type = Regex.Replace(tds[11].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                            item.amount = Regex.Replace(tds[13].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                            item.scope = Regex.Replace(tds[15].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                            
                            item.period = string.Format("{0}到{1}", Regex.Replace(tds[17].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase),
                                Regex.Replace(tds[19].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase));
                            item.status = Regex.Replace(tds[21].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                            
                            Mortgagee mortgagee = new Model.Mortgagee()
                            {
                                seq_no = c,
                                name = Regex.Replace(tds[7].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase),
                                identify_no = Regex.Replace(tds[9].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase)

                            };
                            item.mortgagees.Add(mortgagee);
                            _enterpriseInfo.mortgages.Add(item);
                            
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                throw;
            }
        }
        /// <summary>
        /// 年报
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseReports(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            try
            {
                if (isIndiviual)
                {
                    var request = CreateRequest();
                    var response = request.GetResponseInfo(_requestXml.GetRequestListByName("report_Indiviual"));
                    if (response != null && response.Count > 0 && !string.IsNullOrWhiteSpace(response[0].Data))
                    {
                        responseData = response[0].Data; 
                    }
                }

                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;
                HtmlNode infoTable = rootNode.SelectSingleNode("//table[@id='tableIdStyle']");
                HtmlNodeCollection trList = infoTable.SelectNodes("./tr");
                List<Report> reportList = new List<Report>();

                if (trList != null)
                {
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                        if (tdList != null && tdList.Count > 2)
                        {
                            Report report = new Report();
                            if (tdList[1].Element("a") != null)
                            {
                                string herfStr = tdList[1].Element("a").Attributes["onclick"].Value;
                                report.ex_id = Regex.Split(Regex.Split(herfStr, "cid=")[1], "&")[0];
                                report.report_name = Regex.Replace(tdList[1].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                                report.report_year = Regex.Replace(tdList[1].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase).Length > 4
                                    ? Regex.Replace(tdList[1].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase).Substring(0, 4) : "";
                                report.report_date = Regex.Replace(tdList[2].InnerText, @"(\s+)|(&nbsp;)+", "", RegexOptions.IgnoreCase);
                                if (!reportsNeedToLoad.Any() || reportsNeedToLoad.Contains(report.report_year))
                                {
                                    reportList.Add(report);
                                }
                                
                            }
                        }
                    }
                }
                _enterpriseInfo.reports = reportList;


            }
            catch (Exception ex)
            {
                _enterpriseInfo.reports.Clear();
                Console.WriteLine("Exception when LoadAndParseReports.. " + ex.ToString());
                LogHelper.Error("Exception when LoadAndParseReports.. " + ex.ToString());
                CheckResponseAndLog(responseData);
                throw ex;
            }

        }

        /// <summary>
        /// 解析年报基本信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportBasic(string responseData, Report report)
        {
            try
            {
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);

                HtmlNode rootNode = document.DocumentNode;
                var divNode = rootNode.SelectSingleNode("//div[@id='sifapanding']/div[@id='qufenkuang']");
                HtmlNodeCollection trList = divNode.SelectNodes("//tr");
                
                if (trList != null)
                {
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection thList = rowNode.SelectNodes("./th");
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");

                        if (thList != null && tdList != null && thList.Count == tdList.Count)
                        {
                            for (int i = 0; i < thList.Count; i++)
                            {
                                var lblName = Regex.Replace(thList[i].InnerText, @"(\s+)|(&nbsp;)+", "");
                                var lblValue = Regex.Replace(tdList[i].InnerText, @"(\s+)|(&nbsp;)+|(null)+", "", RegexOptions.IgnoreCase);
                                switch (lblName)
                                {
                                    case "营业执照注册号":
                                    case "注册号":
                                        report.reg_no = lblValue;
                                        break;
                                    case "统一社会信用代码":
                                        report.credit_no = lblValue;
                                        break;
                                    case "注册号/统一社会信用代码":
                                    case "统一社会信用代码/注册号":
                                        if (lblValue.Length == 18)
                                            report.credit_no = lblValue;
                                        else
                                            report.reg_no = lblValue;
                                        break;
                                    case "名称":
                                    case "企业名称":
                                        report.name = lblValue.Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                                        break;
                                    case "经营者姓名":
                                        report.oper_name = lblValue;
                                        break;
                                    case "联系电话":
                                    case "企业联系电话":
                                        if (_enterpriseInfo.credit_no != "91110108MA0026D315")
                                        {
                                            report.telephone = Regex.Replace(tdList[i].InnerText, @"([\r\n\t]+)|(&nbsp;)+|(null)+", "", RegexOptions.IgnoreCase); ;
                                        }
                                        else
                                        {
                                            report.telephone = "";
                                        }
                                        break;
                                    case "企业通信地址":
                                        report.address = lblValue;
                                        break;
                                    case "邮政编码":
                                        report.zip_code = lblValue;
                                        break;
                                    case "电子邮箱":
                                        report.email = lblValue;
                                        break;
                                    case "企业是否有投资信息或购买其他公司股权":
                                        report.if_invest = lblValue;
                                        break;
                                    case "是否有网站或网店":
                                        report.if_website = lblValue;
                                        break;
                                    case "企业经营状态":
                                        report.status = lblValue;
                                        break;
                                    case "从业人数":
                                        report.collegues_num = lblValue;
                                        break;
                                    case "有限责任公司本年度是否发生股东股权转让":
                                        report.if_equity = lblValue;
                                        break;
                                    case "资金数额":
                                    case "资产总额":
                                        report.total_equity = lblValue;
                                        break;
                                    case "所有者权益合计":
                                        report.profit_reta = lblValue;
                                        break;
                                    case "销售额或营业收入":
                                    case "销售总额":
                                        report.sale_income = lblValue;
                                        break;
                                    case "利润总额":
                                        report.profit_total = lblValue;
                                        break;
                                    case "营业总收入中主营业务收入":
                                        report.serv_fare_income = lblValue;
                                        break;
                                    case "净利润":
                                        report.net_amount = lblValue;
                                        break;
                                    case "纳税总额":
                                        report.tax_total = lblValue;
                                        break;
                                    case "负债总额":
                                        report.debit_amount = lblValue;
                                        break;
                                    default:
                                        break;
                                }
                            }
                        }
                    }
                }
                var table = rootNode.SelectSingleNode("//div[@id='sifapanding']/div[@id='qufenkuang']/table[@class='detailsList']");
                HtmlNodeCollection ths = table.SelectNodes("./th");
                HtmlNodeCollection tds = table.SelectNodes("./td");
                if (ths != null && tds != null && ths.Count == tds.Count)
                {
                    for (var i = 0; i < ths.Count; i++)
                    {
                        var lblName = Regex.Replace(ths[i].InnerText, @"(\s+)|(&nbsp;)+", "");
                        var lblValue = Regex.Replace(tds[i].InnerText, @"(\s+)|(&nbsp;)+|(null)+", "", RegexOptions.IgnoreCase);
                        switch (lblName)
                        {
                            case "企业是否有投资信息或购买其他公司股权":
                                report.if_invest = lblValue;
                                break;
                            case "从业人数":
                                report.collegues_num = lblValue;
                                break;
                            default:
                                break;
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
               // throw ex;
            }
        }

        /// <summary>
        /// 解析年报网站
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportWebsite(string responseData, Report report)
        {
            try
            {
                var request = CreateRequest();
                report.websites = new List<WebsiteItem>();
                LoadAndParseWebsiteByPage(responseData, report);

                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;

                HtmlNode pageNode = rootNode.SelectSingleNode("//input[@id='pagescount']");
                if (pageNode != null)
                {
                    int pagescount = Int32.Parse(pageNode.Attributes["value"].Value);
                    if (pagescount > 1)
                    {
                        for (int i = 2; i <= pagescount; i++)
                        {
                            request.AddOrUpdateRequestParameter("pageNos", i + "");
                            List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("report_website"));
                            if (responseList != null && responseList.Count > 0)
                            {
                                LoadAndParseWebsiteByPage(responseList[0].Data, report);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                throw;
            }
        }

        /// <summary>
        /// 按页更新年报网站
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseWebsiteByPage(string responseData, Report report)
        {
            try
            {
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;
                HtmlNodeCollection trList = rootNode.SelectNodes("//tr");

                if (trList != null)
                {
                    int i = report.websites.Count;
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                        if (tdList != null && tdList.Count > 2)
                        {
                            WebsiteItem item = new WebsiteItem();
                            item.seq_no = ++i;
                            item.web_type = Regex.Replace(tdList[0].InnerText, @"(\s+)|(&nbsp;)+", "");
                            item.web_name = Regex.Replace(tdList[1].InnerText, @"(\s+)|(&nbsp;)+", "");
                            item.web_url = Regex.Replace(tdList[2].InnerText, @"(\s+)|(&nbsp;)+", "");

                            report.websites.Add(item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                throw;
            }
        }
        /// <summary>
        /// 解析年报股东
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportPartner(string responseData, Report report)
        {
            try
            {
                var request = CreateRequest();
                report.partners = new List<Partner>();
                LoadAndParsePartnerByPage(responseData, report);

                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;

                HtmlNode pageNode = rootNode.SelectSingleNode("//input[@id='pagescount']");
                if (pageNode != null)
                {
                    int pagescount = Int32.Parse(pageNode.Attributes["value"].Value);
                    if (pagescount > 1)
                    {
                        for (int i = 2; i <= pagescount; i++)
                        {
                            request.AddOrUpdateRequestParameter("reportId", report.ex_id);
                            request.AddOrUpdateRequestParameter("report_stockInfo_pageNos", i + "");
                            List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("report_stockInfo"));
                            if (responseList != null && responseList.Count > 0)
                            {
                                LoadAndParsePartnerByPage(responseList[0].Data, report);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                throw;
            }
        }

        /// <summary>
        /// 按页更新年报股东
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParsePartnerByPage(string responseData, Report report)
        {
            try
            {
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);

                HtmlNode rootNode = document.DocumentNode;
                HtmlNodeCollection trList = rootNode.SelectNodes("//tr");

                List<Partner> itemList = new List<Partner>();
                int i = report.partners.Count;

                if (trList != null)
                {
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                        if (tdList != null && tdList.Count > 5)
                        {
                            Partner item = new Partner();

                            item.seq_no = ++i;
                            item.stock_name = tdList[0].InnerText.Trim();
                            item.stock_type = tdList[6].InnerText.Trim();
                            item.identify_no = "";
                            item.identify_type = "";
                            item.stock_percent = "";
                            item.ex_id = "";
                            item.real_capi_items = new List<RealCapiItem>();
                            item.should_capi_items = new List<ShouldCapiItem>();

                            ShouldCapiItem sItem = new ShouldCapiItem();
                            sItem.shoud_capi = tdList[1].InnerText.Trim();
                            sItem.should_capi_date = tdList[2].InnerText.Trim();
                            sItem.invest_type = tdList[3].InnerText.Trim();
                            item.should_capi_items.Add(sItem);

                            RealCapiItem rItem = new RealCapiItem();
                            rItem.real_capi = tdList[4].InnerText.Trim();
                            rItem.real_capi_date = tdList[5].InnerText.Trim();
                            rItem.invest_type = tdList[3].InnerText.Trim();
                            item.real_capi_items.Add(rItem);

                            report.partners.Add(item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                throw;
            }
        }
        /// <summary>
        /// 解析年报投资
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportInvest(string responseData, Report report)
        {
            try
            {
                Thread.Sleep(100);
                var request = CreateRequest();
                report.invest_items = new List<InvestItem>();
                LoadAndParseInvestByPage(responseData, report);

                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;

                HtmlNode pageNode = rootNode.SelectSingleNode("//input[@id='pagescount']");
                if (pageNode != null)
                {
                    int pagescount = Int32.Parse(pageNode.Attributes["value"].Value);
                    if (pagescount > 1)
                    {
                        for (int i = 2; i <= pagescount; i++)
                        {
                            request.AddOrUpdateRequestParameter("reportId", report.ex_id);
                            request.AddOrUpdateRequestParameter("report_investInfo_pageNos", i + "");
                            List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("report_investInfo"));
                            if (responseList != null && responseList.Count > 0)
                            {
                                LoadAndParseInvestByPage(responseList[0].Data, report);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                throw;
            }

        }

        /// <summary>
        /// 按页更新年报投资
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseInvestByPage(string responseData, Report report)
        {
            try
            {
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;
                HtmlNodeCollection trList = rootNode.SelectNodes("//tr");

                if (trList != null)
                {
                    int i = report.invest_items.Count;
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                        if (tdList != null && tdList.Count > 1)
                        {
                            InvestItem item = new InvestItem();

                            item.seq_no = ++i;
                            item.invest_name = tdList[0].InnerText.Trim();
                            item.invest_reg_no = tdList[1].InnerText.Trim();

                            report.invest_items.Add(item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                throw;
            }
        }
        /// <summary>
        /// 解析年报对外提供保证担保信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportExternalGuarantee(string responseData, Report report)
        {
            try
            {
                var request = CreateRequest();
                LoadAndParseExternalGuaranteeByPage(responseData, report);

                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;

                HtmlNode pageNode = rootNode.SelectSingleNode("//input[@id='pagescount']");
                if (pageNode != null)
                {
                    int pagescount = Int32.Parse(pageNode.Attributes["value"].Value);
                    if (pagescount > 1)
                    {
                        for (int i = 2; i <= pagescount; i++)
                        {
                            request.AddOrUpdateRequestParameter("reportId", report.ex_id);
                            request.AddOrUpdateRequestParameter("report_externalGuaranteeInfo_pageNos", i + "");
                            List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("report_externalGuaranteeInfo"));
                            if (responseList != null && responseList.Count > 0)
                            {
                                LoadAndParseExternalGuaranteeByPage(responseList[0].Data, report);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                throw;
            }

        }
        /// <summary>
        /// 按页更新年报对外提供保证担保信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseExternalGuaranteeByPage(string responseData, Report report)
        {
            try
            {
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;
                HtmlNodeCollection trList = rootNode.SelectNodes("//tr");

                if (trList != null)
                {
                    int i = report.external_guarantees.Count;
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                        if (tdList != null && tdList.Count > 1)
                        {
                            ExternalGuarantee item = new ExternalGuarantee();

                            item.seq_no = ++i;
                            item.creditor = Regex.Replace(tdList[0].InnerText, @"(\s+)|(&nbsp;)+", "");
                            item.debtor = Regex.Replace(tdList[1].InnerText, @"(\s+)|(&nbsp;)+", "");
                            item.type = Regex.Replace(tdList[2].InnerText, @"(\s+)|(&nbsp;)+", "");
                            item.amount = Regex.Replace(tdList[3].InnerText, @"(\s+)|(&nbsp;)+", "");
                            item.period = Regex.Replace(tdList[4].InnerText, @"(\s+)|(&nbsp;)+", "");
                            item.guarantee_time = Regex.Replace(tdList[5].InnerText, @"(\s+)|(&nbsp;)+", "");
                            item.guarantee_type = Regex.Replace(tdList[6].InnerText, @"(\s+)|(&nbsp;)+", "");
                            item.guarantee_scope = Regex.Replace(tdList[7].InnerText, @"(\s+)|(&nbsp;)+", "");

                            report.external_guarantees.Add(item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                throw;
            }
        }
        /// <summary>
        /// 解析年报股权变更
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportGuQuanBianGeng(string responseData, Report report)
        {
            try
            {
                var request = CreateRequest();
                LoadAndParseGuQuanBianGengByPage(responseData, report);

                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;

                HtmlNode pageNode = rootNode.SelectSingleNode("//input[@id='pagescount']");
                if (pageNode != null)
                {
                    int pagescount = Int32.Parse(pageNode.Attributes["value"].Value);
                    if (pagescount > 1)
                    {
                        for (int i = 2; i <= pagescount; i++)
                        {
                            request.AddOrUpdateRequestParameter("reportId", report.ex_id);
                            request.AddOrUpdateRequestParameter("report_guquanbiangeng_pageNos", i + "");
                            List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("report_guquanbiangengInfo"));
                            if (responseList != null && responseList.Count > 0)
                            {
                                LoadAndParseGuQuanBianGengByPage(responseList[0].Data, report);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                throw;
            }

        }
        private void LoadAndParseGuQuanBianGengByPage(string responseData, Report report)
        {
            try
            {
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;
                HtmlNodeCollection trList = rootNode.SelectNodes("//tr");

                if (trList != null)
                {
                    int i = report.stock_changes.Count;
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                        if (tdList != null && tdList.Count > 1)
                        {
                            StockChangeItem item = new StockChangeItem();

                            item.seq_no = ++i;
                            item.name = Regex.Replace(tdList[0].InnerText, @"(\s+)|(&nbsp;)+", "");
                            item.before_percent = Regex.Replace(tdList[1].InnerText, @"(\s+)|(&nbsp;)+", "");
                            item.after_percent = Regex.Replace(tdList[2].InnerText, @"(\s+)|(&nbsp;)+", "");
                            item.change_date = Regex.Replace(tdList[3].InnerText, @"(\s+)|(&nbsp;)+", "");

                            report.stock_changes.Add(item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                throw;
            }
        }
        /// <summary>
        /// 解析年报修改记录
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportUpdateRecords(string responseData, Report report)
        {
            try
            {
                var request = CreateRequest();
                LoadAndParseUpdateRecordsByPage(responseData, report);

                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;

                HtmlNode pageNode = rootNode.SelectSingleNode("//input[@id='pagescount']");
                if (pageNode != null)
                {
                    int pagescount = Int32.Parse(pageNode.Attributes["value"].Value);
                    if (pagescount > 1)
                    {
                        for (int i = 2; i <= pagescount; i++)
                        {
                            request.AddOrUpdateRequestParameter("report_updateRecordsInfo_year", report.report_year);
                            request.AddOrUpdateRequestParameter("reportId", report.ex_id);
                            request.AddOrUpdateRequestParameter("report_updateRecordsInfo_pageNos", i + "");
                            List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("report_updateRecordsInfo"));
                            if (responseList != null && responseList.Count > 0)
                            {
                                LoadAndParseUpdateRecordsByPage(responseList[0].Data, report);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                throw;
            }

        }
        /// <summary>
        /// 按页更新年报修改记录
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseUpdateRecordsByPage(string responseData, Report report)
        {
            try
            {
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;
                HtmlNodeCollection trList = rootNode.SelectNodes("//tr");

                if (trList != null)
                {
                    int i = report.update_records.Count;
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                        if (tdList != null && tdList.Count > 1)
                        {
                            UpdateRecord item = new UpdateRecord();

                            item.seq_no = ++i;
                            item.update_item = Regex.Replace(tdList[1].InnerText, @"(\s+)|(&nbsp;)+", "");
                            item.before_update = Regex.Replace(tdList[2].InnerText, @"(\s+)|(&nbsp;)+", "");
                            item.after_update = Regex.Replace(tdList[3].InnerText, @"(\s+)|(&nbsp;)+", "");
                            item.update_date = Regex.Replace(tdList[4].InnerText, @"(\s+)|(&nbsp;)+", "");

                            report.update_records.Add(item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                throw;
            }
        }
        /// <summary>
        /// 股东及出资信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseGuDongJiChuZi(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            try
            {
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;
                HtmlNodeCollection tables = rootNode.SelectNodes("//table[@id='tableIdStyle']");
                if (tables == null) return;
                HtmlNodeCollection trList = tables[0].SelectNodes("./tr");
                List<FinancialContribution> list = new List<FinancialContribution>();

                if (trList != null)
                {
                    var c = 1;
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                        if (tdList != null && tdList.Count > 3)
                        {
                            FinancialContribution fc = new FinancialContribution();


                            fc.seq_no = c;
                            fc.investor_name = Regex.Replace(tdList[0].InnerText, @"(\s)+|(&nbsp;)+", "");
                            fc.investor_type = "";
                            fc.total_should_capi = Regex.Replace(tdList[1].InnerText, @"(\s)+|(&nbsp;)+", "");
                            fc.total_real_capi = Regex.Replace(tdList[2].InnerText, @"(\s)+|(&nbsp;)+", "");
                            var sciTds = tdList[3].SelectNodes("./table/tr/td");
                            if (sciTds != null)
                            {
                                FinancialContribution.ShouldCapiItem sci = new FinancialContribution.ShouldCapiItem()
                                {
                                    should_invest_type = Regex.Replace(sciTds[0].InnerText, @"(\s)+|(&nbsp;)+", ""),
                                    should_capi = Regex.Replace(sciTds[1].InnerText, @"(\s)+|(&nbsp;)+", ""),
                                    should_invest_date = Regex.Replace(sciTds[2].InnerText, @"(\s)+|(&nbsp;)+", "")
                                };
                                fc.should_capi_items.Add(sci);
                            }
                            var rciTds = tdList[4].SelectNodes("./table/tr/td");
                            if (rciTds != null)
                            {
                                for (int i = 0; i < rciTds.Count(); i+=3)
                                {
                                    FinancialContribution.RealCapiItem rci = new FinancialContribution.RealCapiItem()
                                    {
                                        real_invest_type = Regex.Replace(rciTds[i].InnerText, @"(\s)+|(&nbsp;)+", ""),
                                        real_capi = Regex.Replace(rciTds[1 + i].InnerText, @"(\s)+|(&nbsp;)+", ""),
                                        real_invest_date = Regex.Replace(rciTds[2 + i].InnerText, @"(\s)+|(&nbsp;)+", "")
                                    };

                                    fc.real_capi_items.Add(rci);
                                }
                               
                            }

                            _enterpriseInfo.financial_contributions.Add(fc);
                            c++;

                        }
                    }
                }

                if (tables.Count < 2) { return; }
                var trList2 = tables[1].SelectNodes("./tr");
                if(trList2!=null&&trList2.Count>2)
                {
                    foreach (var tr2 in trList2)
                    {
                        var tds2 = tr2.SelectNodes("./td");
                        if (tds2 != null && tds2.Count > 4)
                        {
                            UpdateRecord ur = new UpdateRecord() 
                            {
                                seq_no = int.Parse(Regex.Replace(tds2[0].InnerText, @"(\s)+|(&nbsp;)+", "")),
                                update_item = Regex.Replace(tds2[1].InnerText, @"(\s)+|(&nbsp;)+", ""),
                                update_date = Regex.Replace(tds2[2].InnerText, @"(\s)+|(&nbsp;)+", ""),
                                before_update = Regex.Replace(tds2[3].InnerText, @"(\s)+|(&nbsp;)+", ""),
                                after_update = Regex.Replace(tds2[4].InnerText, @"(\s)+|(&nbsp;)+", "")
                            };
                            _enterpriseInfo.update_records.Add(ur);
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                CheckResponseAndLog(responseData);
                throw ex;
            }
        }

        /// <summary>
        /// 股权变更信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseGuQuanBianGeng(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            HtmlNode xzcfTable = rootNode.SelectSingleNode("//table[@id='tableIdStyle']");
            HtmlNodeCollection trList = xzcfTable.SelectNodes("./tr");

            List<StockChangeItem> list = new List<StockChangeItem>();
            if (trList != null)
            {
                foreach (HtmlNode rowNode in trList)
                {
                    HtmlNodeCollection tdList = rowNode.SelectNodes("./td");

                    if (tdList != null && tdList.Count > 3)
                    {
                        StockChangeItem item = new StockChangeItem()
                        {
                            seq_no = int.Parse(tdList[0].InnerText.Replace("&nbsp;", "")),
                            name = tdList[1].InnerText.Replace("&nbsp;", ""),
                            before_percent = tdList[2].InnerText.Replace("&nbsp;", ""),
                            after_percent = tdList[3].InnerText.Replace("&nbsp;", ""),
                            change_date = tdList[4].InnerText.Replace("&nbsp;", "")
                        };

                        list.Add(item);
                    }
                }
            }
            _enterpriseInfo.stock_changes = list;
        }

        /// <summary>
        /// 行政许可信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseXingZhengXuKe(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            HtmlNode xzxkTable = rootNode.SelectSingleNode("//table[@id='tableIdStyle']");
            HtmlNodeCollection trList = xzxkTable.SelectNodes("./tr");

            List<LicenseInfo> list = new List<LicenseInfo>();
            if (trList != null)
            {
                foreach (HtmlNode rowNode in trList)
                {
                    HtmlNodeCollection tdList = rowNode.SelectNodes("./td");

                    if (tdList != null && tdList.Count > 3)
                    {
                        LicenseInfo item = new LicenseInfo()
                        {
                            seq_no = int.Parse(tdList[0].InnerText.Replace("&nbsp;", "").Trim()),
                            number = tdList[1].InnerText.Replace("&nbsp;", "").Trim(),
                            name = tdList[2].InnerText.Replace("&nbsp;", "").Trim(),
                            start_date = tdList[3].InnerText.Replace("&nbsp;", "").Trim(),
                            end_date = tdList[4].InnerText.Replace("&nbsp;", "").Trim(),
                            department = tdList[5].InnerText.Replace("&nbsp;", "").Trim(),
                            content = tdList[6].InnerText.Replace("&nbsp;", "").Trim(),
                            status = tdList[7].InnerText.Replace("&nbsp;", "").Trim()

                        };

                        var aNode = tdList[8].SelectSingleNode("./a");
                        if (aNode != null)
                        {
                            var aHref = aNode.Attributes["href"] != null ? string.Empty : aNode.Attributes["href"].Value;
                            var request = CreateRequest();
                            request.AddOrUpdateRequestParameter("xingzhengxukeDetailUrl", aHref);
                            //List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("xingzhengxukeDetail"));
                            //if (responseList != null)
                            //{

                            //}

                            list.Add(item);
                        }
                    }
                }

                _enterpriseInfo.licenses = list;
            }
        }
        private void LoadAndParseXingZhengXuKeDetail(string responseData) { }

        /// <summary>
        /// 知识产权信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseZhiShiChanQuan(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            HtmlNode xzxkTable = rootNode.SelectSingleNode("//table[@id='tableIdStyle']");
            HtmlNodeCollection trList = xzxkTable.SelectNodes("./tr");

            if (trList != null)
            {
                foreach (HtmlNode rowNode in trList)
                {
                    HtmlNodeCollection tdList = rowNode.SelectNodes("./td");

                    if (tdList != null && tdList.Count > 8)
                    {
                        KnowledgeProperty item = new KnowledgeProperty()
                        {
                            seq_no = int.Parse(Regex.Replace(tdList[0].InnerText, @"(\s)+|(&nbsp;)+", "")),
                            number = Regex.Replace(tdList[1].InnerText, @"(\s)+|(&nbsp;)+", ""),
                            name = Regex.Replace(tdList[2].InnerText, @"(\s)+|(&nbsp;)+", ""),
                            type = Regex.Replace(tdList[3].InnerText, @"(\s)+|(&nbsp;)+", ""),
                            pledgor = Regex.Replace(tdList[4].InnerText, @"(\s)+|(&nbsp;)+", ""),
                            pawnee = Regex.Replace(tdList[5].InnerText, @"(\s)+|(&nbsp;)+", ""),
                            period = Regex.Replace(tdList[6].InnerText, @"(\s)+|(&nbsp;)+", ""),
                            status = Regex.Replace(tdList[7].InnerText, @"(\s)+|(&nbsp;)+", "")

                        };
                        _enterpriseInfo.knowledge_properties.Add(item);
                    }
                }
            }
        }
        #region 过滤暗接口应缴实缴
        void CheckPartnerDetail(List<Partner> partners)
        {
            if (string.IsNullOrWhiteSpace(_enterpriseInfo.regist_capi) || partners == null || !partners.Any()) return;
            decimal should_total = 0;
            foreach (var p in _enterpriseInfo.partners)
            {
                if (p.should_capi_items != null && p.should_capi_items.Any())
                {
                    foreach (var item in p.should_capi_items)
                    {
                        decimal should_capi;
                        var len = item.shoud_capi.IndexOf("万");
                        decimal.TryParse(item.shoud_capi.Substring(0, len.Equals(-1)?item.shoud_capi.Length:len), out should_capi);
                        should_total += should_capi;
                    }
                }
            }
            var len_out = _enterpriseInfo.regist_capi.IndexOf("万");
            if (should_total.ToString() != _enterpriseInfo.regist_capi.Substring(0,len_out))
            {
                _enterpriseInfo.partners.ForEach(p => p.should_capi_items.Clear());
                _enterpriseInfo.partners.ForEach(p => p.real_capi_items.Clear());
            }
        }
        #endregion
    }

}
