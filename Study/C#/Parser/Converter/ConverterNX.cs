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
    public class ConverterNX : IConverter
    {
        DataRequest _request;
        RequestInfo _requestInfo;
        RequestXml _requestXml;
        EnterpriseInfo _enterpriseInfo = new EnterpriseInfo();
        List<AbnormalInfo> _abnormals = new List<AbnormalInfo>();
        List<CheckupInfo> _checkups = new List<CheckupInfo>();
        List<string> reportsNeedToLoad = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings.Get("ReportsNeedToLoad"))
? new List<string>() : ConfigurationManager.AppSettings.Get("ReportsNeedToLoad").Split(',').ToList();
        // 用于更新参数
        string nbxhUpdated = "";

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
            _request.AddOrUpdateRequestParameter("currPage", "1");

            //解析基本信息：基本信息
            List<ResponseInfo> responseList = GetResponseInfo(_requestXml.GetRequestListByGroup("basicInfo"));
            ParseResponse(responseList);

            // 解析基本信息：股东信息、变更信息
            responseList = GetResponseInfo(_requestXml.GetRequestListByGroup("basic1"));
            ParseResponse(responseList);

            // [开始使用新的参数]
            // 主要人员信息、经营异常信息、抽查检查信息【股东详情中更改了编码】、解析年报  
            responseList = GetResponseInfo(_requestXml.GetRequestListByGroup("basic2"));
            ParseResponse(responseList);
            responseList = GetResponseInfo(_requestXml.GetRequestListByGroup("basic3"));
            ParseResponse(responseList);
            // 分支机构信息
            responseList = GetResponseInfo(_requestXml.GetRequestListByName("branch"));
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
                    //case "updateParam":
                    //    LoadAndParseUpdateParam(responseInfo.Data);
                    //    break;
                    case "basicInfo":
                    case "gtbasicInfo":
                        LoadAndParseBasicInfo(responseInfo.Data, _enterpriseInfo);
                        break;
                    case "partner":
                        LoadAndParsePartner(responseInfo.Data, _enterpriseInfo);
                        break;
                    case "alter":
                    case "gtAlter":
                        LoadAndParseAlter(responseInfo.Data, _enterpriseInfo);
                        break;
                    case "employee":
                        LoadAndParseEmployee(responseInfo.Data, _enterpriseInfo);
                        break;
                    case "branch":
                        LoadAndParseBranch(responseInfo.Data, _enterpriseInfo);
                        break;
                    case "jingyin":
                        LoadAndParseJingyin(responseInfo.Data, _abnormals);
                        break;
                    case "check":
                        LoadAndParseCheck(responseInfo.Data, _checkups);
                        break;
                    case "report":
                    case "gtReport":
                        if (responseInfo.Name == "report")
                        {
                            LoadAndParseReport(responseInfo.Data, _enterpriseInfo);
                        }
                        else
                        {
                            LoadAndParseReport(responseInfo.Data, _enterpriseInfo, "gtReport");
                        }
                        break;
                    case "dongchandiya":
                        LoadAndParseDongChanDiYa(responseInfo.Data,_enterpriseInfo);
                        break;
                    case "guquanchuzhi":
                        LoadAndParseGuQuanChuZhi(responseInfo.Data,_enterpriseInfo);
                        break;
                    case "xingzhengchufa":
                        LoadAndParseXingZhengChuFa(responseInfo.Data, _enterpriseInfo);
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
                    case "zhishichanquan":
                        LoadAndParseZhiShiChanQuan(responseInfo.Data, _enterpriseInfo);
                        break;
                    case "sifaxiezhu":
                        LoadAndParseSiFaXiezhu(responseInfo.Data, _enterpriseInfo);
                        break;
                    default:
                        break;
                }
            }
        }

       private void LoadAndParseUpdateParam(string html)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(html);
            HtmlNode rootNode = document.DocumentNode;
            nbxhUpdated = rootNode.SelectSingleNode("//input[@id='nbxh']").Attributes["value"].Value;
            this._request.AddOrUpdateRequestParameter("nbxhUpdated", nbxhUpdated);
        }

       #region 解析工商公示信息：基本信息
        /// <summary>
        /// 解析工商公示信息：基本信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseBasicInfo(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            // 基本信息
            HtmlNode table = rootNode.SelectSingleNode("//table");

            HtmlNodeCollection tdList = table.SelectNodes("./tr/td");
            for (int i = 0; i < tdList.Count; i++)
            {
                var scopeReplace = tdList[i].InnerText.Split('：', ':')[0] + ":";
                var scopeReplace1 = tdList[i].InnerText.Split('：', ':')[0] + "：";
                switch (tdList[i].InnerText.Split('：', ':')[0].Replace("&nbsp;", "").Replace("·", "").Trim())
                {
                    case "注册号":
                        _enterpriseInfo.reg_no = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                        break;
                    case "统一社会信用代码":
                        _enterpriseInfo.credit_no = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                        break;
                    case "注册号/统一社会信用代码":
                    case "统一社会信用代码/注册号":
                        if (tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "").Length == 18)
                            _enterpriseInfo.credit_no = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                        else
                            _enterpriseInfo.reg_no = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                        break;
                    case "名称":
                    case "企业名称":
                        _enterpriseInfo.name = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                        break;
                    case "类型":
                        _enterpriseInfo.econ_kind = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("null", "").Replace("NULL", "");
                        break;
                    case "法定代表人":
                    case "负责人":
                    case "股东":
                    case "经营者":
                    case "执行事务合伙人":
                    case "投资人":
                        _enterpriseInfo.oper_name = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("null", "").Replace("NULL", "");
                        break;
                    case "住所":
                    case "经营场所":
                    case "营业场所":
                    case "主要经营场所":
                        Address address = new Address();
                        address.name = "注册地址";
                        address.address = tdList[i].InnerText.Split('：', ':')[1].Trim();
                        address.postcode = "";
                        _enterpriseInfo.addresses.Add(address);
                        break;
                    case "注册资金":
                    case "注册资本":
                    case "成员出资总额":
                        _enterpriseInfo.regist_capi = tdList[i].InnerText.Split('：', ':')[1].Trim();
                        break;
                    case "成立日期":
                    case "登记日期":
                    case "注册日期":
                        _enterpriseInfo.start_date = tdList[i].InnerText.Split('：', ':')[1].Trim();
                        break;
                    case "营业期限自":
                    case "经营期限自":
                    case "合伙期限自":
                        _enterpriseInfo.term_start = tdList[i].InnerText.Split('：', ':')[1].Trim();
                        break;
                    case "营业期限至":
                    case "经营期限至":
                    case "合伙期限至":
                        _enterpriseInfo.term_end = tdList[i].InnerText.Split('：', ':')[1].Trim();
                        break;
                    case "经营范围":
                    case "业务范围":
                        _enterpriseInfo.scope = tdList[i].InnerText.Replace(scopeReplace, "").Replace(scopeReplace1, "").Replace("null", "").Replace("NULL", "");
                        break;
                    case "登记机关":
                        _enterpriseInfo.belong_org = tdList[i].InnerText.Split('：', ':')[1].Trim();
                        break;
                    case "核准日期":
                        _enterpriseInfo.check_date = tdList[i].InnerText.Split('：', ':')[1].Trim();
                        break;
                    case "登记状态":
                        _enterpriseInfo.status = tdList[i].InnerText.Split('：', ':')[1].Trim();
                        break;
                    case "吊销日期":
                    case "注销日期":
                        _enterpriseInfo.end_date = tdList[i].InnerText.Split('：', ':')[1].Trim();
                        break;
                    case "组成形式":
                        _enterpriseInfo.type_desc = tdList[i].InnerText.Split('：', ':')[1].Trim();
                        break;
                    default:
                        break;
                }
            }

        }
       #endregion

        #region 解析工商公示信息：股东信息

        /// <summary>
        /// 解析工商公示信息：股东信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParsePartner(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var node = rootNode.SelectSingleNode("//input[@id='countPage']");
            if (node != null)
            {
                var pageCount = int.Parse(node.Attributes["value"].Value);
                if (pageCount >= 1)
                {
                    for (int index = 1; index <= pageCount; index++)
                    {
                        var request = CreateRequest();
                        request.AddOrUpdateRequestParameter("currPage", index.ToString());
                        List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("partner"));
                        responseData = responseList[0].Data;
                        List<Partner> partnerList = new List<Partner>();

                        document = new HtmlDocument();
                        document.LoadHtml(responseData);
                        rootNode = document.DocumentNode;
                        HtmlNodeCollection trList = rootNode.SelectSingleNode("//table").SelectNodes("//tr");
                        if (trList != null && trList.Count >= 1)
                        {
                            int j = 1;
                            foreach (HtmlNode rowNode in trList)
                            {
                                if (rowNode.Attributes["class"] != null && rowNode.Attributes["class"].Value == "partner_com_top") continue;
                                HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                                if (tdList != null && tdList.Count > 3)
                                {
                                    Partner partner = new Partner();
                                    partner.identify_no = tdList[4].InnerText.Trim();
                                    partner.identify_type = tdList[3].InnerText.Trim();

                                    partner.seq_no = ((index-1)*5)+j++;
                                    partner.stock_name = tdList[1].InnerText.Trim();
                                    partner.stock_percent = "";
                                    partner.stock_type = tdList[2].InnerText.Trim();
                                    partner.should_capi_items = new List<ShouldCapiItem>();
                                    partner.real_capi_items = new List<RealCapiItem>();

                                    //解析股东详情
                                    if (tdList.Count > 4)
                                    {
                                        var a = tdList.Last().SelectSingleNode("./a");
                                        if (a != null)
                                        {
                                            var array = a.Attributes["onclick"].Value.Replace("detail(", "").Replace(")", "").Replace("'", "").Split(',');
                                            string investorId = array[0];
                                            partner.ex_id = investorId;
                                            _request.AddOrUpdateRequestParameter("xh", investorId);
                                            List<ResponseInfo> reponseList = GetResponseInfo(_requestXml.GetRequestListByName("investor_detials"));
                                            if (reponseList != null && reponseList.Count() > 0)
                                            {
                                                LoadAndParseInvestorDetails(partner, reponseList[0].Data);
                                            }
                                        }

                                    }

                                    partnerList.Add(partner);
                                }
                            }
                        }
                        _enterpriseInfo.partners.AddRange(partnerList);
                    }
                }
            }

        }
        #endregion

        #region 解析股东详情
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
            var divs = rootNode.SelectNodes("//div[@class='partner_com bdb']");
            if (divs.Count != 3) return;
            var shouldTable = divs[0].SelectSingleNode("./table");
            HtmlNodeCollection trListTotal= shouldTable.SelectNodes("./tr");
            foreach (var tr in trListTotal)
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

            var infoTable = divs[1].SelectSingleNode("./table");
            HtmlNodeCollection trList = infoTable.SelectNodes("./tr");

            foreach (HtmlNode rowNode in trList)
            {
                if (rowNode.Attributes["class"] != null && rowNode.Attributes["class"].Value == "partner_com_top") continue;
                HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                ShouldCapiItem sItem = new ShouldCapiItem();
                sItem.shoud_capi =tdList[1].InnerText.Trim();
                sItem.should_capi_date = tdList[2].InnerText.Trim();
                sItem.invest_type = tdList[0].InnerText.Trim();
                partner.should_capi_items.Add(sItem);
            }


            infoTable = divs[2].SelectSingleNode("./table");
            trList = infoTable.SelectNodes("./tr");

            foreach (HtmlNode rowNode in trList)
            {
                if (rowNode.Attributes["class"] != null && rowNode.Attributes["class"].Value == "partner_com_top") continue;
                HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                ShouldCapiItem sItem = new ShouldCapiItem();
                RealCapiItem rItem = new RealCapiItem();
                rItem.real_capi = convertNumberToCash(tdList[1].InnerText.Trim());
                rItem.real_capi_date = tdList[2].InnerText.Trim();
                rItem.invest_type = removeLastComma(tdList[0].InnerText.Trim());
                partner.real_capi_items.Add(rItem);
            }

        }
        #endregion

        #region 解析工商公示信息：变更信息
        /// <summary>
        /// 解析工商公示信息：变更信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseAlter(string responseData, EnterpriseInfo _enterpriseInfo,int page=1)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var node = rootNode.SelectSingleNode("//input[@id='countPage']");
            if(node!=null)
            {
                var pageCount = int.Parse(node.Attributes["value"].Value);
                if(pageCount>=1)
                {
                    for (int index = 1; index <= pageCount; index++)
                    {
                        var request = CreateRequest();
                        request.AddOrUpdateRequestParameter("currPage", index.ToString());
                        List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("alter"));
                        List<ChangeRecord> changeRecordList = new List<ChangeRecord>();
                        responseData = responseList[0].Data;
                        document = new HtmlDocument();
                        document.LoadHtml(responseData);
                        rootNode = document.DocumentNode;
                        HtmlNodeCollection trList = rootNode.SelectSingleNode("//table").SelectNodes("//tr");
                        int k = 1;
                        foreach (HtmlNode rowNode in trList)
                        {
                            if (rowNode.Attributes["class"] != null && rowNode.Attributes["class"].Value == "partner_com_top") continue;
                            HtmlNodeCollection tddList = rowNode.SelectNodes("./td");
                            ChangeRecord changeRecord = new ChangeRecord();
                            if (tddList != null && tddList.Count > 3)
                            {
                                changeRecord.change_item = tddList[1].InnerText.Trim();
                                changeRecord.before_content = tddList[2].InnerText.Trim();
                                changeRecord.after_content = tddList[3].InnerText.Trim();
                                changeRecord.change_date = tddList[4].InnerText.Trim();
                                changeRecord.seq_no = ((index-1)*5)+k++;
                                changeRecordList.Add(changeRecord);
                            }
                        }
                        _enterpriseInfo.changerecords.AddRange(changeRecordList);
                    }
                }
            }
        }
        #endregion

        #region 解析工商公示信息：主要人员信息
        /// <summary>
        /// 解析工商公示信息：主要人员信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseEmployee(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var nodes = rootNode.SelectNodes("//ul[@class='info_name clearfix']/li");
            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    Employee emp = new Employee();
                    emp.seq_no = _enterpriseInfo.employees.Count() + 1;
                    emp.job_title = node.SelectSingleNode("./p").InnerText;
                    if (string.IsNullOrWhiteSpace(emp.job_title))
                    {
                        continue;
                    }
                    emp.name = node.InnerText.Replace(emp.job_title, "");
                    _enterpriseInfo.employees.Add(emp);
                }
            }
        }
        #endregion

        #region 解析工商公示信息：分支机构信息
        /// <summary>
        /// 解析工商公示信息：分支机构信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseBranch(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var nodes = rootNode.SelectNodes("//ul[@class='info_name info_name_width clearfix']/li");
            List<Branch> branchList = new List<Branch>();
            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    Branch branch = new Branch();
                    branch.seq_no =  branchList.Count + 1;
                    branch.belong_org = string.Empty;
                    branch.name = node.SelectSingleNode("./h3") == null ? string.Empty : node.SelectSingleNode("./h3").InnerText;
                    branch.oper_name = "";
                    branch.reg_no = node.SelectSingleNode("./p") == null ? string.Empty : node.SelectSingleNode("./p").InnerText.Replace("统一社会信用代码/注册号：", "").Trim();
                    branchList.Add(branch);
                }
            }
            _enterpriseInfo.branches = branchList;
        }
        #endregion

        #region 解析工商公示信息：经营异常信息

        /// <summary>
        /// 解析工商公示信息：经营异常信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseJingyin(string responseData, List<AbnormalInfo> _abnormals)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            HtmlNodeCollection yichangTrList = rootNode.SelectSingleNode("//table").SelectNodes("//tr");
            if (yichangTrList != null)
            {
                foreach (HtmlNode rowNode in yichangTrList)
                {
                    HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                    if (tdList != null && tdList.Count > 3)
                    {
                        if (rowNode.Attributes["class"] != null && rowNode.Attributes["class"].Value == "partner_com_top") continue;
                        AbnormalInfo item = new AbnormalInfo();
                        item.in_reason = tdList[1].InnerText;
                        item.in_date = tdList[2].InnerText;
                        item.out_reason = tdList[4].InnerText;
                        item.out_date = tdList[5].InnerText;
                        item.department = tdList[3].InnerText;

                        _abnormals.Add(item);
                    }
                }
            }
        }
        #endregion

        #region 解析工商公示信息：抽查检查信息
        /// <summary>
        /// 解析工商公示信息：抽查检查信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseCheck(string responseData, List<CheckupInfo> _checkups)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            HtmlNodeCollection jianchaTrList = rootNode.SelectSingleNode("//table").SelectNodes("//tr");
            if (jianchaTrList != null)
            {
                foreach (HtmlNode rowNode in jianchaTrList)
                {
                    if (rowNode.Attributes["class"] != null && rowNode.Attributes["class"].Value == "partner_com_top") continue;
                    HtmlNodeCollection tdList = rowNode.SelectNodes("./td");

                    if (tdList != null && tdList.Count > 3)
                    {
                        CheckupInfo item = new CheckupInfo();
                        item.department = tdList[1].InnerText;
                        item.type = tdList[2].InnerText.Replace("<!-- 这里原版本写死为抽查，没有相应的字检，目前所有值为1 即抽查 -->","").Replace("\r","").Replace("\n","").Replace("\t","");
                        item.date = tdList[3].InnerText;
                        item.result = tdList[4].InnerText;

                        _checkups.Add(item);
                    }
                }
            }
        }
        #endregion

        #region 加载动产抵押信息
        /// <summary>
        /// 加载动产抵押信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseDongChanDiYa(string responseData, EnterpriseInfo _enterpriseInfo)
        {
           HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var node = rootNode.SelectSingleNode("//input[@id='countPage']");
            if (node != null)
            {
                var pageCount = int.Parse(node.Attributes["value"].Value);
                if (pageCount >= 1)
                {
                    for (int index = 1; index <= pageCount; index++)
                    {
                        var request = CreateRequest();
                        request.AddOrUpdateRequestParameter("currPage", index.ToString());
                        List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("dongchandiya"));
                        List<ChangeRecord> changeRecordList = new List<ChangeRecord>();
                        responseData = responseList[0].Data;
                        document = new HtmlDocument();
                        document.LoadHtml(responseData);
                        rootNode = document.DocumentNode;
                        var trs = rootNode.SelectNodes("//tr");
                        if (trs != null && trs.Count >= 2)
                        {
                            foreach (var tr in trs)
                            {
                                if (tr.Attributes["class"] != null && tr.Attributes["class"].Value == "partner_com_top") continue;
                                var tds = tr.SelectNodes("./td");
                                if (tds != null && tds.Count > 7)
                                {
                                    MortgageInfo mortgage = new MortgageInfo();
                                    mortgage.seq_no = _enterpriseInfo.mortgages.Count + 1;
                                    mortgage.number = Regex.Replace(tds[1].InnerText, "\\s+(&nbsp;)*", "");
                                    mortgage.date = Regex.Replace(tds[2].InnerText, "\\s+(&nbsp;)*", "");
                                    mortgage.department = Regex.Replace(tds[3].InnerText, "\\s+(&nbsp;)*", "");
                                    mortgage.amount = Regex.Replace(tds[4].InnerText, "\\s+(&nbsp;)*", "");
                                    mortgage.status = Regex.Replace(tds[5].InnerText, "\\s+(&nbsp;)*", "");
                                    mortgage.public_date = Regex.Replace(tds[6].InnerText, "\\s+(&nbsp;)*", "");
                                    var aNode = tds.Last().SelectSingleNode("./a");
                                    if (aNode != null)
                                    {
                                        var onclick = aNode.Attributes["onclick"] == null ? string.Empty : aNode.Attributes["onclick"].Value;
                                        if (!string.IsNullOrWhiteSpace(onclick))
                                        {
                                            var detailMsg = onclick.Replace("detail('", "").Replace("')", "");
                                            if (!string.IsNullOrWhiteSpace(detailMsg))
                                            {
                                                _request.AddOrUpdateRequestParameter("dongchandiyaDetail_htnbxh", detailMsg);
                                                List<ResponseInfo> responses = _request.GetResponseInfo(_requestXml.GetRequestListByGroup("dongchandiya"));
                                                if (responses != null && responses.Any())
                                                {
                                                    LoadAndParseDongChaDiYaDetail(responses[0], mortgage);
                                                }
                                            }
                                        }
                                    }
                                    _enterpriseInfo.mortgages.Add(mortgage);
                                }
                            }
                        }
                    }
                }
            }
        
        }

        #endregion

        #region 加载动产抵押信息
        /// <summary>
        /// 加载动产抵押信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseSiFaXiezhu(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var node = rootNode.SelectSingleNode("//input[@id='countPage']");
            if (node != null)
            {
                var pageCount = int.Parse(node.Attributes["value"].Value);
                if (pageCount >= 1)
                {
                    for (int index = 1; index <= pageCount; index++)
                    {
                        var request = CreateRequest();
                        request.AddOrUpdateRequestParameter("currPage", index.ToString());
                        List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("sifaxiezhu"));
                        responseData = responseList[0].Data;
                        document = new HtmlDocument();
                        document.LoadHtml(responseData);
                        rootNode = document.DocumentNode;
                        var trs = rootNode.SelectNodes("//tr");
                        if (trs != null && trs.Count >= 2)
                        {
                            foreach (var tr in trs)
                            {
                                if (tr.Attributes["class"] != null && tr.Attributes["class"].Value == "partner_com_top") continue;
                                var tds = tr.SelectNodes("./td");
                                if (tds != null && tds.Count == 7)
                                {
                                    JudicialFreeze freeze = new JudicialFreeze();
                                    freeze.seq_no = _enterpriseInfo.mortgages.Count + 1;
                                    freeze.be_executed_person = Regex.Replace(tds[1].InnerText, "\\s+(&nbsp;)*", "");
                                    freeze.amount = Regex.Replace(tds[2].InnerText, "\\s+(&nbsp;)*", "");
                                    freeze.executive_court = Regex.Replace(tds[3].InnerText, "\\s+(&nbsp;)*", "");
                                    freeze.number = Regex.Replace(tds[4].InnerText, "\\s+(&nbsp;)*", "");
                                    freeze.status = Regex.Replace(tds[5].InnerText, "\\s+(&nbsp;)*", "");
                                    var aNode = tds.Last().SelectSingleNode("./a");
                                    if (aNode != null)
                                    {
                                        var onclick = aNode.Attributes["onclick"] == null ? string.Empty : aNode.Attributes["onclick"].Value;
                                        if (!string.IsNullOrWhiteSpace(onclick))
                                        {
                                            var detailMsg = onclick.Split(',')[0].Replace("detail('", "").Replace("'", "");
                                            if (!string.IsNullOrWhiteSpace(detailMsg))
                                            {
                                                _request.AddOrUpdateRequestParameter("djxh", detailMsg);

                                                List<ResponseInfo> responses =onclick.Split(',')[0].Contains("1")?
                                                    _request.GetResponseInfo(_requestXml.GetRequestListByGroup("sifaxiezhudetail"))
                                                    : _request.GetResponseInfo(_requestXml.GetRequestListByGroup("sifaxiezhudetailbg"));
                                                if (responses != null && responses.Any())
                                                {
                                                    LoadAndParseSiFaXieZuDetail(responses[0], freeze);
                                                }
                                            }
                                        }
                                    }
                                    _enterpriseInfo.judicial_freezes.Add(freeze);
                                }
                            }
                        }
                    }
                }
            }

        }


        private void LoadAndParseSiFaXieZuDetail(ResponseInfo responseInfo, JudicialFreeze freeze)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseInfo.Data);
            HtmlNode rootNode = document.DocumentNode;
            HtmlNodeCollection tables = rootNode.SelectNodes("//table");
            if (tables == null) return;
            for (var index = 0; index < tables.Count; index++)
            {
                var table = tables[index];
                var rows = table.SelectNodes("./tr");
                var titletable = table.ParentNode.SelectSingleNode("./div");
                if (rows != null && rows.Count > 0)
                {
                    if (titletable.InnerText.Contains("冻结情况"))
                    {
                        freeze.type = "股权冻结";
                        foreach (var row in rows)
                        {
                            if (rows != null && rows.Count > 1)
                            {
                                JudicialFreezeDetail freezeDetail = new JudicialFreezeDetail();
                                for (int i = 0; i < rows.Count; i++)
                                {
                                    HtmlNodeCollection thList = rows[i].SelectNodes("./td[@class='table_h3']");
                                    HtmlNodeCollection tdList = rows[i].SelectNodes("./td[@class='txt_left']");
                                    if (thList != null && tdList != null && thList.Count == tdList.Count)
                                    {
                                        for (int j = 0; j < thList.Count; j++)
                                        {
                                            switch (thList[j].InnerText.Trim())
                                            {
                                                case "执行法院":
                                                    freezeDetail.execute_court = tdList[j].InnerText.Trim();
                                                    break;
                                                case "执行事项":
                                                    freezeDetail.assist_item = tdList[j].InnerText.Trim();
                                                    break;
                                                case "执行裁定书文号":
                                                    freezeDetail.adjudicate_no = tdList[j].InnerText.Trim();
                                                    break;
                                                case "执行通知书文号":
                                                    freezeDetail.notice_no = tdList[j].InnerText.Trim();
                                                    break;
                                                case "被执行人":
                                                    freezeDetail.assist_name = tdList[j].InnerText.Trim();
                                                    break;
                                                case "被执行人持有股份、其他投资权益的数额":
                                                case "被执行人持有股权、其它投资权益的数额":
                                                    freezeDetail.freeze_amount = tdList[j].InnerText.Trim();
                                                    break;
                                                case "被执行人证件种类":
                                                case "被执行人证照种类":
                                                    freezeDetail.assist_ident_type = tdList[j].InnerText.Trim();
                                                    break;
                                                case "被执行人证件号码":
                                                case "被执行人证照号码":
                                                    freezeDetail.assist_ident_no = tdList[j].InnerText.Trim();
                                                    break;
                                                case "冻结期限自":
                                                    freezeDetail.freeze_start_date = tdList[j].InnerText.Trim();
                                                    break;
                                                case "冻结期限至":
                                                    freezeDetail.freeze_end_date = tdList[j].InnerText.Trim();
                                                    break;
                                                case "冻结期限":
                                                    freezeDetail.freeze_year_month = tdList[j].InnerText.Trim();
                                                    break;
                                                case "公示日期":
                                                    freezeDetail.public_date = tdList[j].InnerText.Trim();
                                                    break;
                                            }
                                        }
                                    }
                                }
                                freeze.detail = freezeDetail;
                            }
                        }

                    }
                    else if (titletable.InnerText.Contains("股东变更信息"))
                    {
                        freeze.type = "股权变更";
                        if (rows != null && rows.Count > 1)
                        {
                            JudicialFreezePartnerChangeDetail freezeDetail = new JudicialFreezePartnerChangeDetail();
                            for (int i = 0; i < rows.Count; i++)
                            {
                                HtmlNodeCollection thList = rows[i].SelectNodes("./td[@class='table_h3']");
                                HtmlNodeCollection tdList = rows[i].SelectNodes("./td[@class='txt_left']");
                                if (thList != null && tdList != null && thList.Count == tdList.Count)
                                {
                                    for (int j = 0; j < thList.Count; j++)
                                    {
                                        switch (thList[j].InnerText.Trim())
                                        {
                                            case "执行法院":
                                                freezeDetail.execute_court = tdList[j].InnerText.Trim();
                                                break;
                                            case "执行事项":
                                                freezeDetail.assist_item = tdList[j].InnerText.Trim();
                                                break;
                                            case "执行裁定书文号":
                                                freezeDetail.adjudicate_no = tdList[j].InnerText.Trim();
                                                break;
                                            case "执行通知书文号":
                                                freezeDetail.notice_no = tdList[j].InnerText.Trim();
                                                break;
                                            case "被执行人":
                                                freezeDetail.assist_name = tdList[j].InnerText.Trim();
                                                break;
                                            case "被执行人持有股份、其他投资权益的数额":
                                            case "被执行人持有股权、其它投资权益的数额":
                                            case "被执行人持有股权数额":
                                                freezeDetail.freeze_amount = tdList[j].InnerText.Trim();
                                                break;
                                            case "被执行人证件种类":
                                            case "被执行人证照种类":
                                                freezeDetail.assist_ident_type = tdList[j].InnerText.Trim();
                                                break;
                                            case "被执行人证件号码":
                                            case "被执行人证照号码":
                                                freezeDetail.assist_ident_no = tdList[j].InnerText.Trim();
                                                break;
                                            case "受让人":
                                                freezeDetail.assignee = tdList[j].InnerText.Trim();
                                                break;
                                            case "协助执行日期":
                                                freezeDetail.xz_execute_date = tdList[j].InnerText.Trim();
                                                break;
                                            case "受让人证件种类":
                                            case "受让人证照种类":
                                                freezeDetail.assignee_ident_type = tdList[j].InnerText.Trim();
                                                break;
                                            case "受让人证件号码":
                                            case "受让人证照号码":
                                                freezeDetail.assignee_ident_no = tdList[j].InnerText.Trim();
                                                break;
                                        }
                                    }
                                }
                            }
                            freeze.pc_freeze_detail = freezeDetail;
                        }
                    }
                }
            }
        }

        #endregion

        #region 解析动产抵押详情
        /// <summary>
        /// 解析动产抵押详情
        /// </summary>
        /// <param name="responseInfo"></param>
        /// <param name="mortgageInfo"></param>
        private void LoadAndParseDongChaDiYaDetail(ResponseInfo responseInfo, MortgageInfo mortgageInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseInfo.Data);
            HtmlNode rootNode = document.DocumentNode;
            HtmlNodeCollection tables = rootNode.SelectNodes("//table");
            if (tables == null) return;
            for (var index = 0; index < tables.Count; index++)
            {
                var table = tables[index];
                var rows = table.SelectNodes("./tr");
                var titletable = table.ParentNode.SelectSingleNode("./div");
                if (rows != null && rows.Count > 0)
                {
                    if (titletable.InnerText.Contains("抵押权人概况信息"))
                    {
                        foreach (var row in rows)
                        {
                            if (row.Attributes["class"] != null && row.Attributes["class"].Value == "partner_com_top") continue;
                            var cells = row.SelectNodes("./td");
                            if (cells == null || cells.Count < 4) continue;
                            Mortgagee mortgagee = new Mortgagee();
                            mortgagee.seq_no = mortgageInfo.mortgagees.Count + 1;
                            mortgagee.name = cells[1].InnerText;
                            mortgagee.identify_type = cells[2].InnerText;
                            mortgagee.identify_no = cells[3].InnerText;
                            mortgageInfo.mortgagees.Add(mortgagee);
                        }
                    }

                }
                if (rows != null)
                {
                    titletable = table.ParentNode.SelectSingleNode("./div");
                    if (titletable.InnerText.Contains("被担保债权概况信息"))
                    {
                        foreach (HtmlNode rowNode in rows)
                        {
                            HtmlNodeCollection thList = rowNode.SelectNodes("./td[@class='table_h3']");
                            HtmlNodeCollection tdList = rowNode.SelectNodes("./td[@class='txt_left']");

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
                if (rows != null && index > 1)
                {
                    titletable = table.ParentNode.SelectSingleNode("./div");
                    if (titletable.InnerText.Contains("抵押物概况信息"))
                    {
                        foreach (var row in rows)
                        {
                            if (row.Attributes["class"] != null && row.Attributes["class"].Value == "partner_com_top") continue;
                            var cells = row.SelectNodes("./td");
                            if (cells == null || cells.Count < 4) continue;
                            Guarantee guarantee = new Guarantee();
                            guarantee.seq_no = mortgageInfo.guarantees.Count + 1;
                            guarantee.name = cells[1].InnerText;
                            guarantee.belong_to = cells[2].InnerText;
                            guarantee.desc = cells[3].InnerText;
                            guarantee.remarks = cells[4].InnerText;
                            mortgageInfo.guarantees.Add(guarantee);
                        }
                    }
                }
            }
        }
        #endregion

        #region 加载股权出质信息
        /// <summary>
        /// 加载股权出质信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        void LoadAndParseGuQuanChuZhi(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var node = rootNode.SelectSingleNode("//input[@id='countPage']");
            if (node != null)
            {
                var pageCount = int.Parse(node.Attributes["value"].Value);
                if (pageCount >= 1)
                {
                    for (int index = 1; index <= pageCount; index++)
                    {
                        var request = CreateRequest();
                        request.AddOrUpdateRequestParameter("currPage", index.ToString());
                        List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("guquanchuzhi"));
                        responseData = responseList[0].Data;
                        document = new HtmlDocument();
                        document.LoadHtml(responseData);
                        rootNode = document.DocumentNode;
                        var trs = rootNode.SelectNodes("//tr");
                        if (trs != null && trs.Count >= 2)
                        {
                            foreach (var tr in trs)
                            {
                                if (tr.Attributes["class"] != null && tr.Attributes["class"].Value == "partner_com_top") continue;
                                var tds = tr.SelectNodes("./td");
                                if (tds != null && tds.Count > 10)
                                {
                                    EquityQuality euityQuality = new EquityQuality();
                                    euityQuality.seq_no = _enterpriseInfo.equity_qualities.Count+1;
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
                                    euityQuality.public_date = tds[9].InnerText;
                                    euityQuality.remark = "";


                                    //var aNode = tds[10].SelectSingleNode("./a");
                                    //if (aNode != null)
                                    //{
                                    //    var onclick = aNode.Attributes["onclick"] == null ? string.Empty : aNode.Attributes["onclick"].Value;
                                    //    if (!string.IsNullOrWhiteSpace(onclick))
                                    //    {
                                    //        var arr = onclick.Split('\'');
                                    //        var detailMsg = (arr == null || arr.Length < 2) ? null : arr[1];
                                    //        if (!string.IsNullOrWhiteSpace(detailMsg))
                                    //        {
                                    //            //加载变更信息
                                    //        }
                                    //    }
                                    //}
                                    _enterpriseInfo.equity_qualities.Add(euityQuality);
                                }
                            }
                        }

                    }
                }
            }
        }
        #endregion

        #region 加载行政处罚信息
        /// <summary>
        /// 加载行政处罚信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        void LoadAndParseXingZhengChuFa(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var table = rootNode.SelectSingleNode("//table");
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null )
                {
                    foreach (var tr in trs)
                    {
                        if (tr.Attributes["class"] != null && tr.Attributes["class"].Value == "partner_com_top") continue;
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Count > 7)
                        {
                            AdministrativePunishment ap = new AdministrativePunishment()
                            {
                                reg_no = _enterpriseInfo.reg_no,
                                oper_name = _enterpriseInfo.oper_name,
                                name = _enterpriseInfo.name
                            };
                            ap.seq_no = int.Parse(tds[0].InnerText);
                            ap.number = Regex.Replace(tds[1].InnerText, @"\s+", "");
                            ap.illegal_type = Regex.Replace(tds[2].InnerText, @"\s+", "");
                            ap.content = Regex.Replace(tds[3].InnerText, @"\s+", "");
                            ap.department = Regex.Replace(tds[4].InnerText, @"\s+", "");
                            ap.date = Regex.Replace(tds[5].InnerText, @"\s+", "");
                            ap.public_date = Regex.Replace(tds[6].InnerText, @"\s+", "");
                            var aNode = tds.Last().SelectSingleNode("./a");
                            if (aNode != null)
                            {
                                var onclick = aNode.Attributes.Contains("href") ? aNode.Attributes["href"].Value : string.Empty;
                                if (!string.IsNullOrWhiteSpace(onclick))
                                {
                                    _request.AddOrUpdateRequestParameter("xingzhengchufa_detail_url", onclick);
                                        var responseList = this.GetResponseInfo(_requestXml.GetRequestListByName("xingzhengchufa_detail"));
                                        if (responseList != null && responseList.Any())
                                        {
                                            this.LoadAndParsePublishmenDetail(responseList.First().Data, ap);
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

        #region 解析行政处罚详情
        /// <summary>
        /// 解析行政处罚详情
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="item"></param>
        void LoadAndParsePublishmenDetail(string responseData, AdministrativePunishment item)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var table = rootNode.SelectSingleNode("//div[@class='info_table ']/table");
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
                                var span = td.SelectSingleNode("./span");
                                var title = span.InnerText.TrimEnd(new char[] { '：' });
                                switch (title)
                                {
                                    case "名称":
                                        item.name = td.InnerText.Replace(span.InnerText, "").Replace("\r", "").Replace("\n", "").Replace("\t", "");
                                        break;
                                    case "统一社会信用代码/注册号":
                                        item.reg_no = td.InnerText.Replace(span.InnerText, "").Replace("\r", "").Replace("\n", "").Replace("\t", "");
                                        break;
                                    case "法定代表人（负责人）姓名":
                                        item.oper_name = td.InnerText.Replace(span.InnerText, "").Replace("\r", "").Replace("\n", "").Replace("\t", "");
                                        break;
                                    default:
                                        break;
                                }
                            }
                        }
                    }
                }
            }
            var div = rootNode.SelectSingleNode("//div[@class='books_bg mgt20']");
            if (div != null)
                item.description = div.InnerHtml;
        }
        #endregion

        #region 解析年报
        /// <summary>
        /// 解析年报
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseReport(string responseData, EnterpriseInfo _enterpriseInfo,string type="report")
        {
            try
            {
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;

                List<Report> reportList = new List<Report>();

                HtmlNode table = rootNode.SelectSingleNode("//table");
                if (table != null)
                {
                    HtmlNodeCollection trList = table.SelectNodes("./tr");
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                        if (tdList != null && tdList.Count > 2)
                        {
                            var aNode = tdList.Last().SelectSingleNode("./a");
                            if (aNode != null)
                            {
                                var aHref = aNode.Attributes["onclick"] == null ? string.Empty : aNode.Attributes["onclick"].Value;
                                if (!string.IsNullOrWhiteSpace(aHref))
                                {
                                    Report report = new Report();
                                    report.report_name = tdList[1].InnerText.Trim();
                                    report.report_year = tdList[1].InnerText.Trim().Length > 4 ? tdList[1].InnerText.Trim().Substring(0, 4) : "";
                                    report.report_date = tdList[2].InnerText.Trim();
                                    if (reportsNeedToLoad.Count == 0 || reportsNeedToLoad.Contains(report.report_year))
                                    {
                                        _request.AddOrUpdateRequestParameter("anCheYear", report.report_year);
                                        // 加载解析网站、股东、对外投资
                                        var responseList = GetResponseInfo(_requestXml.GetRequestListByGroup("reportDetail"));
                                        ParseReport(responseList, report);

                                        reportList.Add(report);
                                    }
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
                Console.WriteLine("Exception when LoadAndParseReport.." + ex.ToString());
                LogHelper.Error("Exception when LoadAndParseReport.." + ex.ToString());
            }
        }
        #endregion

        #region 解析企业年报
        /// <summary>
        /// 解析企业年报
        /// </summary>
        /// <param name="responseInfoList"></param>
        private void ParseReport(List<ResponseInfo> responseInfoList, Report report)
        {
            foreach (ResponseInfo responseInfo in responseInfoList)
            {
                switch (responseInfo.Name)
                {
                    case "reportBasic":
                        LoadAndParseReportBasic(responseInfo.Data, report);
                        break;
                    case "reportWebsite":
                        LoadAndParseReportWebsite(responseInfo.Data, report);
                        break;
                    case "reportPartner":
                        LoadAndParseReportPartner(responseInfo.Data, report);
                        break;
                    case "reportInvest":
                        LoadAndParseReportInvest(responseInfo.Data, report);
                        break;
                    case "reportZichan":
                        LoadAndParsereportZichan(responseInfo.Data, report);
                        break;
                    case "reportExternalGuarantee":
                        LoadAndParseReportExternalGuarantee(responseInfo.Data, report);
                        break;
                    case "reportStockChange":
                        LoadAndParseReportStockChange(responseInfo.Data, report);
                        break;
                    case "reportUpdateRecord":
                        LoadAndParseReportUpdateRecord(responseInfo.Data, report);
                        break;
                    case "reportSheBao":
                        this.LoadAndParseSheBaoInfo(responseInfo.Data, report);
                        break;
                    default:
                        break;
                }
            }
        }
        #endregion

        #region ParseReportGT
        private void ParseReportGT(List<ResponseInfo> responseInfoList, Report report)
        {
            foreach (ResponseInfo responseInfo in responseInfoList)
            {
                switch (responseInfo.Name)
                {
                    case "reportBasic":
                        LoadAndParseReportBasicGT(responseInfo.Data, report);
                        break;
                    default:
                        break;
                }
            }
        }
        #endregion

        #region 加载解析年报详细信息
        /// <summary>
        /// 加载解析年报详细信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportBasic(string responseData, Report report)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            report.report_date = rootNode.SelectSingleNode("//em").InnerText.Replace("填报时间：","");
            HtmlNodeCollection tdList = rootNode.SelectNodes("//tr/td");

            for (int i = 0; i < tdList.Count; i++)
            {
                var scopeReplace = tdList[i].InnerText.Split('：', ':')[0] + ":";
                var scopeReplace1 = tdList[i].InnerText.Split('：', ':')[0] + "：";
                switch (tdList[i].InnerText.Split('：', ':')[0].Replace("&nbsp;", "").Replace("·", "").Trim())
                {
                    case "注册号":
                    case "营业执照注册号":
                        report.reg_no = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                        break;
                    case "统一社会信用代码":
                        report.credit_no = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                        break;
                    case "注册号/统一社会信用代码":
                    case "统一社会信用代码/注册号":
                        if (tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "").Length == 18)
                            report.credit_no = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                        else
                            report.reg_no = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                        break;
                    case "企业名称":
                    case "名称":
                        report.name = tdList[i].InnerText.Trim().Split('：', ':')[1].Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                        break;
                    case "企业联系电话":
                    case "联系电话":
                        report.telephone = tdList[i].InnerText.Split('：', ':')[1].Trim();
                        break;
                    case "企业通信地址":
                        report.address = tdList[i].InnerText.Split('：', ':')[1].Trim();
                        break;
                    case "邮政编码":
                        report.zip_code = tdList[i].InnerText.Split('：', ':')[1].Trim();
                        break;
                    case "电子邮箱":
                    case "企业电子邮箱":
                        report.email = tdList[i].InnerText.Split('：', ':')[1].Trim();
                        break;
                    case "企业是否有投资信息或购买其他公司股权":
                    case "企业是否有对外投资设立企业信息":
                    case "是否有投资信息或购买其他公司股权":
                        report.if_invest = tdList[i].InnerText.Split('：', ':')[1].Trim();
                        break;
                    case "是否有网站或网店":
                    case "是否有网站或网点":
                        report.if_website = tdList[i].InnerText.Split('：', ':')[1].Trim();
                        break;
                    case "企业经营状态":
                    case "企业登记状态":
                        report.status = tdList[i].InnerText.Split('：', ':')[1].Trim();
                        break;
                    case "从业人数":
                        report.collegues_num = tdList[i].InnerText.Split('：', ':')[1].Trim();
                        break;
                    case "有限责任公司本年度是否发生股东股权转让":
                        report.if_equity = tdList[i].InnerText.Split('：', ':')[1].Trim();
                        break;
                    case "经营者姓名":
                        report.oper_name = tdList[i].InnerText.Split('：', ':')[1].Trim();
                        break;
                    case "资金数额":
                        report.reg_capi = tdList[i].InnerText.Split('：', ':')[1].Trim();
                        break;
                    default:
                        break;
                }

            }
        }
        #endregion

        #region 加载解析个体年报详细信息
        /// <summary>
        /// 加载解析个体年报详细信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportBasicGT(string responseData, Report report)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var tables = rootNode.SelectNodes("//table");
            foreach (var table in tables)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    var firstTr = trs.FirstOrDefault();
                    var title = firstTr.InnerText;
                    if (title.Contains("红色为修改过的信息项"))
                    {
                        trs.RemoveAt(0);
                        trs.RemoveAt(0);
                        foreach (var tr in trs)
                        {
                            HtmlNodeCollection thList = tr.SelectNodes("./th");
                            HtmlNodeCollection tdList = tr.SelectNodes("./td");

                            if (thList != null && tdList != null && thList.Count == tdList.Count)
                            {
                                for (int i = 0; i < thList.Count; i++)
                                {
                                    switch (thList[i].InnerText.Trim())
                                    {
                                        case "营业执照注册号":
                                            report.reg_no = tdList[i].InnerText.Trim().Replace("&nbsp;", "");
                                            break;
                                        case "名称":
                                            report.name = tdList[i].InnerText.Trim().Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                                            break;
                                        case "联系电话":
                                            report.telephone = tdList[i].InnerText.Trim();
                                            break;
                                        case "从业人数":
                                            report.collegues_num = tdList[i].InnerText.Trim();
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
                    else if (title == "网站或网店信息")
                    {
                        if (trs.Count > 2)
                        {
                            trs.RemoveAt(0);
                            trs.RemoveAt(0);
                            foreach (var tr in trs)
                            {
                                var tds = tr.SelectNodes("./td");
                                if (tds != null && tds.Count > 2)
                                {
                                    WebsiteItem item = new WebsiteItem();

                                    item.seq_no = report.websites.Count + 1;
                                    item.web_type = tds[0].InnerText.Trim();
                                    item.web_name = tds[1].InnerText.Trim();
                                    item.web_url = tds[2].InnerText.Trim();
                                    report.websites.Add(item);
                                }
                            }
                        }
                    }
                    else if (title == "生产经营情况信息")
                    {
                        if (trs.Count > 1)
                        {
                            trs.RemoveAt(0);
                            foreach(var tr in trs)
                            {
                                var ths = tr.SelectNodes("./th");
                                var tds = tr.SelectNodes("./td");
                                if (ths != null && tds != null && ths.Count == tds.Count)
                                {
                                    for (int i = 0; i <= ths.Count - 1; i++)
                                    {
                                        switch (ths[i].InnerText)
                                        { 
                                            case "销售额或营业收入":
                                                report.sale_income = tds[i].InnerText;
                                                break;
                                            case "纳税总额":
                                                report.tax_total = tds[i].InnerText;
                                                break;
                                            default:
                                                break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else if (title == "修改记录")
                    {
                        if (trs.Count > 2)
                        {
                            trs.RemoveAt(0);
                            trs.RemoveAt(0);
                            foreach (var tr in trs)
                            {
                                var tds = tr.SelectNodes("./td");
                                if (tds != null && tds.Count > 4)
                                {
                                    UpdateRecord item = new UpdateRecord();

                                    item.seq_no = report.update_records.Count + 1;
                                    item.update_item = tds[1].InnerText.Trim();
                                    item.before_update = tds[2].InnerText.Trim();
                                    item.after_update = tds[3].InnerText.Trim();
                                    item.update_date = tds[4].InnerText.Trim();
                                    report.update_records.Add(item);
                                }
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析年报网站或网店信息
        /// <summary>
        /// 解析年报网站或网店信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportWebsite(string responseData, Report report)
        {
            List<WebsiteItem> websiteList = new List<WebsiteItem>();

            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            HtmlNodeCollection trList = rootNode.SelectNodes("//li");
            if (trList != null && trList.Count > 0)
            {
                foreach (HtmlNode rowNode in trList)
                {
                    WebsiteItem item = new WebsiteItem();
                    item.seq_no = websiteList.Count + 1;
                    item.web_name = rowNode.SelectSingleNode("./h3").InnerText;
                    var ps = rowNode.SelectNodes("//p");
                    if (ps != null && ps.Count > 1)
                    {
                        item.web_type = ps[0].SelectSingleNode("./span") != null ? ps[0].SelectSingleNode("./span").InnerText.Replace("\r\n", "").Replace("\t", "") : string.Empty;
                        item.web_url = ps[1].SelectSingleNode("./span") != null ? ps[1].SelectSingleNode("./span").InnerText : string.Empty;
                    }
                    websiteList.Add(item);
                }
                report.websites = websiteList;
            }
        }
        #endregion

        #region 解析年报股东及出资信息
        /// <summary>
        /// 解析年报股东及出资信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportPartner(string responseData, Report report)
        {
             HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var node = rootNode.SelectSingleNode("//input[@id='countPage']");
            if (node != null)
            {
                var pageCount = int.Parse(node.Attributes["value"].Value);
                if (pageCount >= 1)
                {
                    for (int index = 1; index <= pageCount; index++)
                    {
                        var request = CreateRequest();
                        request.AddOrUpdateRequestParameter("currPage", index.ToString());
                        List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("reportPartner"));
                        responseData = responseList[0].Data;
                        document = new HtmlDocument();
                        document.LoadHtml(responseData);
                        rootNode = document.DocumentNode;

                        foreach (HtmlNode rowNode in rootNode.SelectNodes("//tr"))
                        {
                            if (rowNode.Attributes["class"] != null && rowNode.Attributes["class"].Value == "partner_com_top") continue;
                            HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                            if (tdList != null && tdList.Count > 5 && tdList[0].InnerText.Trim() != "")
                            {
                                Partner item = new Partner();

                                item.seq_no = report.partners.Count + 1;
                                item.stock_name = tdList[1].InnerText.Trim();
                                item.stock_type = "";
                                item.identify_no = "";
                                item.identify_type = "";
                                item.stock_percent = "";
                                item.ex_id = "";
                                item.real_capi_items = new List<RealCapiItem>();
                                item.should_capi_items = new List<ShouldCapiItem>();

                                ShouldCapiItem sItem = new ShouldCapiItem();
                                sItem.shoud_capi = convertNumberToCash(tdList[2].InnerText.Trim());
                                sItem.should_capi_date = tdList[3].InnerText.Trim();
                                sItem.invest_type = removeLastComma(tdList[4].InnerText.Trim());
                                item.should_capi_items.Add(sItem);

                                RealCapiItem rItem = new RealCapiItem();
                                rItem.real_capi = convertNumberToCash(tdList[5].InnerText.Trim());
                                rItem.real_capi_date = tdList[6].InnerText.Trim();
                                rItem.invest_type = removeLastComma(tdList[7].InnerText.Trim());
                                item.real_capi_items.Add(rItem);

                                report.partners.Add(item);
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析年报股权变更信息
        /// <summary>
        /// 解析年报股权变更信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportStockChange(string responseData, Report report)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var node = rootNode.SelectSingleNode("//input[@id='countPage']");
            if (node != null)
            {
                var pageCount = int.Parse(node.Attributes["value"].Value);
                if (pageCount >= 1)
                {
                    for (int index = 1; index <= pageCount; index++)
                    {
                        var request = CreateRequest();
                        request.AddOrUpdateRequestParameter("currPage", index.ToString());
                        List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("reportStockChange"));
                        responseData = responseList[0].Data;
                        document = new HtmlDocument();
                        document.LoadHtml(responseData);
                        rootNode = document.DocumentNode;

                        foreach (HtmlNode rowNode in rootNode.SelectNodes("//tr"))
                        {
                            if (rowNode.Attributes["class"] != null && rowNode.Attributes["class"].Value == "partner_com_top") continue;
                            HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                            if (tdList != null && tdList.Count == 5 && tdList[1].InnerText.Trim() != "")
                            {
                                StockChangeItem item = new StockChangeItem();

                                item.seq_no = report.stock_changes.Count + 1;
                                item.name = tdList[1].InnerText.Trim();
                                item.before_percent = tdList[2].InnerText.Trim();
                                item.after_percent = tdList[3].InnerText.Trim();
                                item.change_date = tdList[4].InnerText.Trim();
                                report.stock_changes.Add(item);
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析年报修改信息
        /// <summary>
        /// 解析年报修改信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportUpdateRecord(string responseData, Report report)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var node = rootNode.SelectSingleNode("//input[@id='countPage']");
            if (node != null)
            {
                var pageCount = int.Parse(node.Attributes["value"].Value);
                if (pageCount >= 1)
                {
                    for (int index = 1; index <= pageCount; index++)
                    {
                        var request = CreateRequest();
                        request.AddOrUpdateRequestParameter("currPage", index.ToString());
                        List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("reportUpdateRecord"));
                        responseData = responseList[0].Data;
                        document = new HtmlDocument();
                        document.LoadHtml(responseData);
                        rootNode = document.DocumentNode;

                        foreach (HtmlNode rowNode in rootNode.SelectNodes("//tr"))
                        {
                            if (rowNode.Attributes["class"] != null && rowNode.Attributes["class"].Value == "partner_com_top") continue;
                            HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                            if (tdList != null && tdList.Count == 5 && tdList[1].InnerText.Trim() != "")
                            {
                                UpdateRecord item = new UpdateRecord();

                                item.seq_no = report.update_records.Count + 1;
                                item.update_item = tdList[1].InnerText.Trim();
                                item.before_update = tdList[2].InnerText.Trim();
                                item.after_update = tdList[3].InnerText.Trim();
                                item.update_item = tdList[4].InnerText.Trim();
                                report.update_records.Add(item);
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析年报对外提供担保信息
        /// <summary>
        /// 解析年报对外提供担保信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportExternalGuarantee(string responseData, Report report)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var node = rootNode.SelectSingleNode("//input[@id='countPage']");
            if (node != null)
            {
                var pageCount = int.Parse(node.Attributes["value"].Value);
                if (pageCount >= 1)
                {
                    for (int index = 1; index <= pageCount; index++)
                    {
                        var request = CreateRequest();
                        request.AddOrUpdateRequestParameter("currPage", index.ToString());
                        List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("reportExternalGuarantee"));
                        responseData = responseList[0].Data;
                        document = new HtmlDocument();
                        document.LoadHtml(responseData);
                        rootNode = document.DocumentNode;

                        foreach (HtmlNode rowNode in rootNode.SelectNodes("//tr"))
                        {
                            if (rowNode.Attributes["class"] != null && rowNode.Attributes["class"].Value == "partner_com_top") continue;
                            HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                            if (tdList != null && tdList.Count == 5 && tdList[1].InnerText.Trim() != "")
                            {
                                ExternalGuarantee item = new ExternalGuarantee();

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
            }
        }
        #endregion

        #region 解析年报对外出资信息
        /// <summary>
        /// 解析年报对外出资信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportInvest(string responseData, Report report)
        {
            List<InvestItem> investList = new List<InvestItem>();

            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            HtmlNodeCollection trList = rootNode.SelectNodes("//li");
            if (trList != null && trList.Count > 0)
            {
                foreach (HtmlNode rowNode in trList)
                {
                    InvestItem item = new InvestItem();
                    item.seq_no = report.invest_items.Count + 1;
                    item.invest_name = rowNode.SelectSingleNode("./h3") != null ? rowNode.SelectSingleNode("./h3").InnerText : string.Empty;
                    item.invest_reg_no = rowNode.SelectSingleNode("./p/span") != null ? rowNode.SelectSingleNode("./p/span").InnerText : String.Empty;
                    report.invest_items.Add(item);

                }
            }
        }
        #endregion

        #region 解析年报资产信息
        /// <summary>
        /// 解析年报资产信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParsereportZichan(string responseData, Report report)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            HtmlNodeCollection trList = rootNode.SelectSingleNode("//table").SelectNodes("./tr");

            foreach (HtmlNode rowNode in trList)
            {
                HtmlNodeCollection thList = rowNode.SelectNodes("./td[@class='table_h3']");
                HtmlNodeCollection tdList = rowNode.SelectNodes("./td[@class='table_left']");

                if (thList != null && tdList != null && thList.Count == tdList.Count)
                {
                    for (int i = 0; i < thList.Count; i++)
                    {
                        switch (thList[i].InnerText.Trim())
                        {
                            case "资产总额":
                                report.total_equity = removeHtmlBlank(tdList[i].InnerText.Trim());
                                break;
                            case "负债总额":
                                report.debit_amount = removeHtmlBlank(tdList[i].InnerText.Trim());
                                break;
                            case "销售总额":
                            case "营业总收入":
                            case "营业额或营业收入":

                                report.sale_income = removeHtmlBlank(tdList[i].InnerText.Trim());
                                break;
                            case "其中：主营业务收入":
                            case "营业总收入中主营业务收入":
                            case "主营业务收入":
                            case "销售总额中主营业务收入":
                                report.serv_fare_income = removeHtmlBlank(tdList[i].InnerText.Trim());
                                break;
                            case "利润总额":
                                report.profit_total = removeHtmlBlank(tdList[i].InnerText.Trim());
                                break;
                            case "净利润":
                                report.net_amount = removeHtmlBlank(tdList[i].InnerText.Trim());
                                break;
                            case "纳税总额":
                                report.tax_total = removeHtmlBlank(tdList[i].InnerText.Trim());
                                break;
                            case "所有者权益合计":
                                report.profit_reta = removeHtmlBlank(tdList[i].InnerText.Trim());
                                break;
                            default:
                                break;
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析社保信息
        void LoadAndParseSheBaoInfo(string responseData, Report report)
        {

            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            HtmlNodeCollection trList = rootNode.SelectSingleNode("//div[@class='partner_com bdb']").SelectNodes("./table/tr");

            foreach (HtmlNode rowNode in trList)
            {
                HtmlNodeCollection thList = rowNode.SelectNodes("./td[@class='partner_com_top']");
                var tdList = rowNode.SelectNodes("./td").Where(p => !p.Attributes.Contains("class")).ToList();

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
                            case "生育险":
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
                            case "参加城镇职工基本养老保险本期实际缴费基数":
                                report.social_security.bq_yanglaobx_je = tdList[i].InnerText.Trim();
                                break;
                            case "参加失业保险本期实际缴费金额":
                            case "参加失业保险本期实际缴费基数":
                                report.social_security.bq_shiyebx_je = tdList[i].InnerText.Trim();
                                break;
                            case "参加职工基本医疗保险本期实际缴费金额":
                            case "参加职工基本医疗保险本期实际缴费基数":
                                report.social_security.bq_yiliaobx_je = tdList[i].InnerText.Trim();
                                break;
                            case "参加工伤保险本期实际缴费金额":
                            case "参加工伤保险本期实际缴费基数":
                                report.social_security.bq_gongshangbx_je = tdList[i].InnerText.Trim();
                                break;
                            case "参加生育保险本期实际缴费金额":
                            case "参加生育保险本期实际缴费基数":
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

        #region 加载股东及出资信息
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
            var node = rootNode.SelectSingleNode("//input[@id='countPage']");
            if (node != null)
            {
                var pageCount = int.Parse(node.Attributes["value"].Value);
                if (pageCount >= 1)
                {
                    for (int index = 1; index <= pageCount; index++)
                    {
                        var request = CreateRequest();
                        request.AddOrUpdateRequestParameter("currPage", index.ToString());
                        List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("gudongjichuzi"));
                        responseData = responseList[0].Data;
                        document = new HtmlDocument();
                        document.LoadHtml(responseData);
                        rootNode = document.DocumentNode;
                        var table = rootNode.SelectSingleNode("//table");
                        var trs = table.SelectNodes("./tr");
                        if (trs != null && trs.Count > 3)
                        {
                            foreach (var tr in trs)
                            {
                                if (tr.Attributes["class"] != null && tr.Attributes["class"].Value == "partner_com_top") continue;
                                var tds = tr.SelectNodes("./td");
                                if (tds != null && tds.Count > 9)
                                {
                                    FinancialContribution fc = new FinancialContribution();
                                    fc.seq_no = _enterpriseInfo.financial_contributions.Count + 1;
                                    fc.investor_name = Regex.Replace(tds[1].InnerText, "\\s+(&nbsp;)*", "");
                                    fc.total_should_capi = Regex.Replace(tds[2].InnerText, "\\s+(&nbsp;)*", "");
                                    fc.total_real_capi = Regex.Replace(tds[3].InnerText, "\\s+(&nbsp;)*", "");

                                    FinancialContribution.ShouldCapiItem sci = new FinancialContribution.ShouldCapiItem();
                                    sci.should_invest_type = Regex.Replace(tds[4].InnerText, "\\s+(&nbsp;)*", "");
                                    sci.should_capi = Regex.Replace(tds[5].InnerText, "\\s+(&nbsp;)*", "");
                                    sci.should_invest_date = Regex.Replace(tds[6].InnerText, "\\s+(&nbsp;)*", "");
                                    fc.should_capi_items.Add(sci);

                                    FinancialContribution.RealCapiItem rci = new FinancialContribution.RealCapiItem();
                                    rci.real_invest_type = Regex.Replace(tds[7].InnerText, "\\s+(&nbsp;)*", "");
                                    rci.real_capi = Regex.Replace(tds[8].InnerText, "\\s+(&nbsp;)*", "");
                                    rci.real_invest_date = Regex.Replace(tds[9].InnerText, "\\s+(&nbsp;)*", "");
                                    rci.public_date = Regex.Replace(tds[10].InnerText, "\\s+(&nbsp;)*", "");
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

        #region 加载股权变更
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
            var node = rootNode.SelectSingleNode("//input[@id='countPage']");
            if (node != null)
            {
                var pageCount = int.Parse(node.Attributes["value"].Value);
                if (pageCount >= 1)
                {
                    for (int index = 1; index <= pageCount; index++)
                    {
                        var request = CreateRequest();
                        request.AddOrUpdateRequestParameter("currPage", index.ToString());
                        List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("guquanbiangeng"));
                        responseData = responseList[0].Data;
                        document = new HtmlDocument();
                        document.LoadHtml(responseData);
                        rootNode = document.DocumentNode;
                        var table = rootNode.SelectSingleNode("//table");
                        var trs = table.SelectNodes("./tr");
                        if (trs != null && trs.Count >= 2)
                        {
                            foreach (var tr in trs)
                            {
                                if (tr.Attributes["class"] != null && tr.Attributes["class"].Value == "partner_com_top") continue;
                                var tds = tr.SelectNodes("./td");
                                if (tds != null && tds.Count > 4)
                                {
                                    StockChangeItem scItem = new StockChangeItem();
                                    scItem.seq_no = _enterpriseInfo.stock_changes.Count + 1;
                                    scItem.name = Regex.Replace(tds[1].InnerText, "\\s+(&nbsp;)*", "");
                                    scItem.before_percent = Regex.Replace(tds[2].InnerText, "\\s+(&nbsp;)*", "");
                                    scItem.after_percent = Regex.Replace(tds[3].InnerText, "\\s+(&nbsp;)*", "");
                                    scItem.change_date = Regex.Replace(tds[4].InnerText, "\\s+(&nbsp;)*", "");
                                    scItem.public_date = Regex.Replace(tds[5].InnerText, "\\s+(&nbsp;)*", "");
                                    _enterpriseInfo.stock_changes.Add(scItem);
                                }
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region 行政许可信息
        /// <summary>
        /// 行政许可信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        void LoadAndParseXingZhengXuKe(string responseData, EnterpriseInfo _enterpriseInfo)
        { 
            Random random=new Random();
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var node = rootNode.SelectSingleNode("//input[@id='countPage']");
            if (node != null)
            {
                var pageCount = int.Parse(node.Attributes["value"].Value);
                if (pageCount >= 1)
                {
                    for (int index = 1; index <= pageCount; index++)
                    {
                        var request = CreateRequest();
                        request.AddOrUpdateRequestParameter("currPage", index.ToString());
                        request.AddOrUpdateRequestParameter("randomNum", random.NextDouble().ToString());
                        List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("xingzhengxuke"));
                        responseData = responseList[0].Data;
                        document = new HtmlDocument();
                        document.LoadHtml(responseData);
                        rootNode = document.DocumentNode;
                        var table = rootNode.SelectSingleNode("//table");
                        if (table != null)
                        {
                            var trs = table.SelectNodes("./tr");
                            if (trs != null && trs.Count >= 2)
                            {
                                foreach (var tr in trs)
                                {
                                    if (tr.Attributes["class"] != null && tr.Attributes["class"].Value == "partner_com_top") continue;
                                    var tds = tr.SelectNodes("./td");
                                    if (tds != null && tds.Count > 8)
                                    {
                                        LicenseInfo license = new LicenseInfo();
                                        license.seq_no = _enterpriseInfo.licenses.Count+1;
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
            }
        }
        #endregion

        #region 知识产权信息
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
            var node = rootNode.SelectSingleNode("//input[@id='countPage']");
            if (node != null)
            {
                var pageCount = int.Parse(node.Attributes["value"].Value);
                if (pageCount >= 1)
                {
                    for (int index = 1; index <= pageCount; index++)
                    {
                        var request = CreateRequest();
                        request.AddOrUpdateRequestParameter("currPage", index.ToString());
                        List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("zhishichanquan"));
                        responseData = responseList[0].Data;
                        document = new HtmlDocument();
                        document.LoadHtml(responseData);
                        rootNode = document.DocumentNode;
                        var table = rootNode.SelectSingleNode("//table");
                        if (table != null)
                        {
                            var trs = table.SelectNodes("./tr");
                            if (trs != null)
                            {
                                foreach (var tr in trs)
                                {
                                    if (tr.Attributes["class"] != null && tr.Attributes["class"].Value == "partner_com_top") continue;
                                    var tds = tr.SelectNodes("./td");
                                    if (tds != null && tds.Count > 8)
                                    {
                                        KnowledgeProperty kp = new KnowledgeProperty();
                                        kp.seq_no = _enterpriseInfo.knowledge_properties.Count + 1;
                                        kp.number = Regex.Replace(tds[1].InnerText, "\\s+(&nbsp;)*", "");
                                        kp.name = Regex.Replace(tds[2].InnerText, "\\s+(&nbsp;)*", "");
                                        kp.type = Regex.Replace(tds[3].InnerText, "\\s+(&nbsp;)*", "");
                                        kp.pledgor = Regex.Replace(tds[4].InnerText, "\\s+(&nbsp;)*", "");
                                        kp.pawnee = Regex.Replace(tds[5].InnerText, "\\s+(&nbsp;)*", "");
                                        kp.period = Regex.Replace(tds[6].InnerText, "\\s+(&nbsp;)*", "");
                                        kp.status = Regex.Replace(tds[7].InnerText, "\\s+(&nbsp;)*", "");
                                        kp.public_date = Regex.Replace(tds[8].InnerText, "\\s+(&nbsp;)*", "");
                                        _enterpriseInfo.knowledge_properties.Add(kp);
                                    }
                                }
                            }
                        }
                    }
                }
            }
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

        #region convertNumberToCash
        private string convertNumberToCash(string number)
        {
            string result = "";
            if (!string.IsNullOrEmpty(number))
            {
                if (number.Contains("不公示"))
                {
                    result = number;
                }
                else
                {
                    result = number;
                }
            }
            return result;
        }
        #endregion

        #region
        private string removeHtmlBlank(string innerText)
        {
            return innerText.Replace("&nbsp;", "").Trim();
        }

        #endregion

        #region retrieveContent
        private string retrieveContent(HtmlNode td)
        {
            string result = "";
            HtmlNodeCollection div = td.SelectNodes("./div");
            if (div == null)
            {
                result = td.InnerText.Trim();
            }
            else
            {
                for (int i = 0; i < div.Count; i++)
                {
                    HtmlNode tag_a = div[i].SelectSingleNode("./a");
                    if ("收起更多" == tag_a.InnerText.Trim())
                    {
                        string tempText = div[i].InnerText.Trim();
                        result = tempText.Replace("收起更多", "").Trim();
                    }
                }
            }
            return result;
        }
        #endregion

        #region removeLastComma
        private string removeLastComma(string text)
        {
            string result = "";
            if (text.Length >= 0) {
                if (text.EndsWith(",") || text.EndsWith("，")) 
                {
                    result = text.Substring(0, text.Length - 1);
                }
                else
                {
                    result = text;
                }
            }
            return result.Trim();
        }
        #endregion
    }
}
