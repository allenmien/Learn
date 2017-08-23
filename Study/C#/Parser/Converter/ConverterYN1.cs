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
  public   class ConverterYN1 : IConverter
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
        public SummaryEntity ProcessRequestAndParse(RequestInfo requestInfo)
        {
            if (requestInfo.Parameters.ContainsKey("name")) _enterpriseName = requestInfo.Parameters["name"];
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

            List<XElement> requestList = null;
            List<ResponseInfo> responseList = null;
            //解析基本信息：基本信息、股东信息、变更信息、主要人员信息、分支机构信息、经营异常信息、抽查检查信息
            //List<ResponseInfo> responseList = _request.GetResponseInfo(_requestXml.GetRequestListByName("gongshang"));
            //ParseResponse(responseList);
            requestList = _requestXml.GetRequestListByName("gongshang").ToList();
            requestList.AddRange(_requestXml.GetRequestListByName("report"));
            requestList.AddRange(_requestXml.GetRequestListByName("xingzhengchufa"));
            requestList.AddRange(_requestXml.GetRequestListByName("xingzhengxuke"));
            requestList.AddRange(_requestXml.GetRequestListByName("jingyingyichang"));
            //requestList.AddRange(_requestXml.GetRequestListByName("sifa"));
            responseList = this._request.GetResponseInfo(requestList);            
            var basicInfo = responseList.FirstOrDefault(p => p.Name == "gongshang");
            this.LoadBasic_Only(_enterpriseInfo, basicInfo.Data);
            ParseResponse(responseList);
           // Parallel.ForEach(responseList, new ParallelOptions { MaxDegreeOfParallelism = 15 }, responseInfo => this.ParseResponse(responseInfo));

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
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseInfo.Data);
                HtmlNode rootNode = document.DocumentNode;
                this.CheckMessageIsError(rootNode);
                switch (responseInfo.Name)
                {

                    case "gongshang":
                        LoadAndParseTab01(responseInfo.Data, _enterpriseInfo);
                        break;
                    case "report":
                        LoadAndParseReport(responseInfo.Data, _enterpriseInfo);
                        break;
                    case "xingzhengchufa":
                        LoadPunishments(_enterpriseInfo, rootNode);
                        break;
                    case "xingzhengxuke":
                        LoadLicenseInfo(_enterpriseInfo, rootNode);
                        break;
                    case "jingyingyichang":
                        LoadAbnormals(rootNode);
                        break;
                    default:
                        break;
                }
            }
        }
        //private void ParseResponse(ResponseInfo responseInfo)
        //{
        //    HtmlDocument document = new HtmlDocument();
        //    document.LoadHtml(responseInfo.Data);
        //    HtmlNode rootNode = document.DocumentNode;
        //    this.CheckMessageIsError(rootNode);
        //    switch (responseInfo.Name)
        //    {
        //        case "gongshang":
        //            LoadAndParseTab01(responseInfo.Data, _enterpriseInfo);
        //            break;
        //        case "report":
        //            LoadAndParseReport(responseInfo.Data, _enterpriseInfo);
        //            break;
        //        //case "sifa":
        //        //    LoadAndParseFreeze(responseInfo.Data, _enterpriseInfo);
        //        //    break;
        //        default:
        //            break;
        //    }
        //}
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
            LoadBasic(_enterpriseInfo, rootNode);
            LoadEmployeeAndBranch(_enterpriseInfo, rootNode);
            LoadCheckup(rootNode);
            LoadEquityQuality(_enterpriseInfo, rootNode);
            LoadMortgage(_enterpriseInfo, rootNode);
            LoadKnowledgeProperty(rootNode,_enterpriseInfo);
            //Parallel.Invoke(() =>
            //{
            //    LoadBasic(_enterpriseInfo, rootNode);
            //},
            //() =>
            //{
            //    LoadEmployeeAndBranch(_enterpriseInfo, rootNode);
            //},
            //() =>
            //{
            //    LoadAbnormals(rootNode);
            //},
            //() =>
            //{
            //    LoadCheckup(rootNode);
            //},
            //() =>
            //{
            //    LoadEquityQuality(_enterpriseInfo, rootNode);
            //},
            //() =>
            //{
            //    LoadPunishments(_enterpriseInfo, rootNode);
            //},
            //() =>
            //{
            //    LoadAndParseFreeze(rootNode, _enterpriseInfo);
            //},
            // () =>
            // {
            //     LoadMortgage(_enterpriseInfo, rootNode);
            // });
           
        }
        private void LoadLicenseInfo(EnterpriseInfo _enterpriseInfo, HtmlNode rootNode)
        {
            #region 行政许可信息
            List<LicenseInfo> licenseinfolist = new List<LicenseInfo>();

            var div = rootNode.SelectSingleNode("//div[@class='content1']");
            if (div != null)
            {
                HtmlNodeCollection licenseinfoTrList = div.SelectNodes("./table/tr");
                if (licenseinfoTrList != null && licenseinfoTrList.Count > 0)
                {
                    foreach (HtmlNode rowNode in licenseinfoTrList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");

                        if (tdList != null && tdList.Count > 6)
                        {
                            LicenseInfo licenseinfo = new LicenseInfo();
                            licenseinfo.seq_no = licenseinfolist.Count + 1;
                            licenseinfo.number = tdList[1].InnerText.Trim();

                            licenseinfo.name = tdList[2].InnerText.Trim();
                            licenseinfo.start_date = tdList[3].InnerText.Trim();
                            licenseinfo.end_date = tdList[4].InnerText.Trim();
                            licenseinfo.department = tdList[5].InnerText.Trim();
                            licenseinfo.content = tdList[6].InnerText.Trim();        //                    
                            licenseinfolist.Add(licenseinfo);
                        }
                    }
                }
            }
            _enterpriseInfo.licenses = licenseinfolist;
            #endregion
        }
      private void LoadKnowledgeProperty(HtmlNode rootNode, EnterpriseInfo _enterpriseInfo)
        {
            #region 知识产权登记信息
            List<KnowledgeProperty> knowledgelist = new List<KnowledgeProperty>();

            var div = rootNode.SelectSingleNode("//div[@rel='layout-01_14']");
            if (div != null)
            {
                HtmlNodeCollection equityqualityTrList = div.SelectNodes("./div/table/tr");
                if (equityqualityTrList != null && equityqualityTrList.Count > 0)
                {
                    foreach (HtmlNode rowNode in equityqualityTrList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");

                        if (tdList != null && tdList.Count >8)
                        {
                            KnowledgeProperty knowledgepro = new KnowledgeProperty();
                            knowledgepro.seq_no = knowledgelist.Count + 1;
                            knowledgepro.number = tdList[1].InnerText.Trim();

                            knowledgepro.name = tdList[2].InnerText.Trim();
                            knowledgepro.type = tdList[3].InnerText.Trim();
                            knowledgepro.pledgor = tdList[4].InnerText.Trim();
                            knowledgepro.pawnee = tdList[5].InnerText.Trim();
                            knowledgepro.period = tdList[6].InnerText.Trim();               

                            knowledgepro.status = tdList[7].InnerText.Trim();
                            knowledgelist.Add(knowledgepro);
                        }
                    }
                }
            }
            _enterpriseInfo.knowledge_properties = knowledgelist;
            #endregion
        }
        private void LoadAndParseFreeze(HtmlNode rootNode, EnterpriseInfo _enterpriseInfo)
        {
            //#region 司法协助信息
            //var div = rootNode.SelectSingleNode("//div[@rel='layout-06_01_01']");
            //if (div != null)
            //{
            //    foreach (var table in tables)
            //    {
            //        string title = table.SelectNodes("./tr/th")[0].InnerText.Trim();
            //        if (title == "司法股权冻结信息")
            //        {
            //            HtmlNodeCollection trList = table.SelectNodes("./tr");

            //            List<JudicialFreeze> freezes = new List<JudicialFreeze>();
            //            int j = 1;
            //            foreach (HtmlNode rowNode in trList)
            //            {
            //                HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
            //                if (tdList != null && tdList.Count > 2)
            //                {
            //                    JudicialFreeze item = new JudicialFreeze();

            //                    item.seq_no = j++;
            //                    item.be_executed_person = tdList[1].InnerText;
            //                    item.amount = tdList[2].InnerText;
            //                    item.executive_court = tdList[3].InnerText;
            //                    item.number = tdList[4].InnerText;
            //                    item.status = tdList[5].InnerText;

            //                    var match = Regex.Match(tdList[6].InnerHtml, "href=\"(.*?)\"");
            //                    if (match != null && !string.IsNullOrEmpty(match.Value))
            //                    {
            //                        string id = match.Value;
            //                        id = id.Replace("href=\"", "").Replace("\"", "");
            //                        var response = this._request.RequestData(new RequestSetting("get", id, string.Empty, string.Empty, "FreezeDetail"));
            //                        HtmlDocument docDetail = new HtmlDocument();
            //                        docDetail.LoadHtml(response.Data);
            //                        HtmlNode root = docDetail.DocumentNode;
            //                        var tableDetail = root.SelectSingleNode("//div[@class='detail-info']/table");
            //                        if (tableDetail != null)
            //                        {
            //                            var trDetail = tableDetail.SelectNodes("./tr");
            //                            if (trDetail != null)
            //                            {
            //                                LoadAndParseFreezeDetail(item, trDetail);
            //                            }
            //                        }

            //                    }

            //                    freezes.Add(item);
            //                }
            //            }
            //            this._enterpriseInfo.judicial_freezes = freezes;
            //        }
            //    }
            //}
        }
        private void LoadMortgage(EnterpriseInfo _enterpriseInfo, HtmlNode rootNode)
        {
            #region 动产抵押登记信息
            List<MortgageInfo> mortgageInfoList = new List<MortgageInfo>();

            // 动产抵押登记信息
            var div = rootNode.SelectSingleNode("//div[@id='layout-01_04_01']");
            if (div != null)
            {
                HtmlNodeCollection mortgageinfoTrList = div.SelectNodes("./div/table/tr");
                if (mortgageinfoTrList != null && mortgageinfoTrList.Count > 0)
                {

                    foreach (HtmlNode rowNode in mortgageinfoTrList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");

                        if (tdList != null && tdList.Count > 6)
                        {
                            MortgageInfo mortgageinfo = new MortgageInfo();
                            mortgageinfo.seq_no = mortgageInfoList.Count + 1;
                            mortgageinfo.number = tdList[1].InnerText.Trim();
                            mortgageinfo.date = tdList[2].InnerText.Trim();
                            mortgageinfo.department = tdList[3].InnerText.Trim();
                            mortgageinfo.amount = tdList[4].InnerText.Trim();
                            mortgageinfo.status = tdList[5].InnerText.Trim();

                            mortgageInfoList.Add(mortgageinfo);
                            // 加载动产抵押登记详细信息
                            //HtmlNode aNode = tdList[7].SelectSingleNode("./a");
                            //if (aNode != null)
                            //{
                            //    string uuid = aNode.Attributes["onclick"].Value.Replace("ajaxReqMortgage('", "").Replace("')", "");
                            //    var request = CreateRequest();
                            //    request.AddOrUpdateRequestParameter("mortgage_uuid", uuid);
                            //    List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("mortgageInfoDetail"));
                            //    if (responseList != null && responseList.Count > 0)
                            //    {
                            //        LoadAndParseMortgageDetails(mortgageinfo, responseList[0].Data);
                            //    }
                            //}
                        }
                    }
                }
            }
            _enterpriseInfo.mortgages = mortgageInfoList;
            #endregion
        }
        private void LoadPunishments(EnterpriseInfo _enterpriseInfo, HtmlNode rootNode)
        {

            #region 行政处罚信息信息
            // 行政处罚信息信息
            List<AdministrativePunishment> punishmentList = new List<AdministrativePunishment>();
            var div = rootNode.SelectSingleNode("//div[@class='content1']");
            if (div != null)
            {
                HtmlNodeCollection punishmentTrList = div.SelectNodes("./table/tr");
                if (punishmentTrList != null && punishmentTrList.Count > 0)
                {
                    foreach (HtmlNode rowNode in punishmentTrList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");

                        if (tdList != null && tdList.Count > 7)
                        {
                            AdministrativePunishment punish = new AdministrativePunishment();
                            punish.seq_no = punishmentList.Count + 1;
                            punish.number = tdList[1].InnerText.Trim();
                            punish.date = tdList[5].InnerText.Trim();
                            punish.department = tdList[4].InnerText.Trim();
                            punish.illegal_type = tdList[2].InnerText.Trim();
                            punish.content = tdList[3].InnerText.Trim();
                            punish.name = _enterpriseInfo.name;
                            punish.oper_name = _enterpriseInfo.oper_name;
                            punish.reg_no = _enterpriseInfo.reg_no;
                            punishmentList.Add(punish);
                             //加载行政处罚详细信息
                            HtmlNode aNode = tdList[7].SelectSingleNode("./a");
                            if (aNode != null)
                            {
                                string reportHerf = aNode.Attributes["href"].Value;
                                string uuid = Regex.Split(reportHerf, "uuid=")[1];
                                var request = CreateRequest();
                                request.AddOrUpdateRequestParameter("punish_uuid", uuid);
                                List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("punishmentDetail"));
                                if (responseList != null && responseList.Count > 0)
                                {
                                    LoadAndParsePunishDetails(punish, responseList[0].Data);
                                }
                            }
                        }
                    }
                }
            }
            _enterpriseInfo.administrative_punishments = punishmentList;
            #endregion
        }
        private void LoadEquityQuality(EnterpriseInfo _enterpriseInfo, HtmlNode rootNode)
        {
            #region 股权出质登记信息
            // 股权出质登记信息
            List<EquityQuality> equityqualityList = new List<EquityQuality>();

            var div = rootNode.SelectSingleNode("//div[@id='layout-01_03_01']");
            if (div != null)
            {
                HtmlNodeCollection equityqualityTrList = div.SelectNodes("./div/table/tr");
                if (equityqualityTrList != null && equityqualityTrList.Count > 0)
                {
                    foreach (HtmlNode rowNode in equityqualityTrList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");

                        if (tdList != null && tdList.Count > 9)
                        {
                            EquityQuality equityquality = new EquityQuality();
                            equityquality.seq_no = equityqualityList.Count + 1;
                            equityquality.number = tdList[1].InnerText.Trim();

                            equityquality.pledgor = tdList[2].InnerText.Trim();
                            equityquality.pledgor_identify_no = tdList[3].InnerText.Trim();
                            equityquality.pledgor_amount = tdList[4].InnerText.Trim();
                            equityquality.pawnee = tdList[5].InnerText.Trim();
                            equityquality.pawnee_identify_no = tdList[6].InnerText.Trim();
                            equityquality.date = tdList[7].InnerText.Trim();
                            equityquality.status = tdList[8].InnerText.Trim();
                            equityqualityList.Add(equityquality);
                        }
                    }
                }
            }
            _enterpriseInfo.equity_qualities = equityqualityList;
            #endregion
        }
        private void LoadCheckup(HtmlNode rootNode)
        {
            #region 抽查检查信息
            // 抽查检查信息
            HtmlNode yichangDiv = rootNode.SelectSingleNode("//div[@id='layout-01_08_01']");
            HtmlNode yichangTable = yichangDiv.SelectSingleNode("./div/table");
            HtmlNodeCollection jianchaTrList = yichangTable.SelectNodes("./tr");
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
            #endregion
        }
        private void LoadAbnormals(HtmlNode rootNode)
        {
            #region 经营异常信息
            // 经营异常信息
            HtmlNode yichangDiv = rootNode.SelectSingleNode("//div[@class='content1']");
            HtmlNode yichangTable = yichangDiv.SelectSingleNode("./table");
            HtmlNodeCollection yichangTrList = yichangTable.SelectNodes("./tr");
            foreach (HtmlNode rowNode in yichangTrList)
            {
                HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                if (tdList != null && tdList.Count > 6)
                {
                    AbnormalInfo item = new AbnormalInfo();
                    item.in_reason = tdList[1].InnerText;
                    item.in_date = tdList[2].InnerText;
                    item.out_reason = tdList[4].InnerText;
                    item.out_date = tdList[5].InnerText;
                    item.department = tdList[3].InnerText;

                    _abnormals.Add(item);
                }
            }
            #endregion
        }
        private void LoadEmployeeAndBranch(EnterpriseInfo _enterpriseInfo, HtmlNode rootNode)
        {
            #region 主要人员信息、分支机构信息
            //主要人员信息、分支机构信息
            var div = rootNode.SelectSingleNode("//div[@id='layout-01_02_01']");
            if (div != null)
            {
                HtmlNode table = div.SelectSingleNode("./div/table[@class='dlPiece']");
                HtmlNodeCollection trList = table.SelectNodes("./tr");
                int i = 1;
                List<Employee> employeeList = new List<Employee>();
                foreach (HtmlNode rowNode in trList)
                {
                    HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                    if (tdList == null) continue;
                    foreach (var td in tdList)
                    {
                        var ul = td.SelectSingleNode("./ul");
                        if (ul == null) continue;
                        Employee employee = new Employee();
                        employee.seq_no = i++;
                        employee.name = ul.SelectSingleNode("./li[@class='greyB1']") != null ? ul.SelectSingleNode("./li[@class='greyB1']").InnerText : string.Empty;
                        employee.job_title = ul.SelectSingleNode("./li[@class='greyB2']") != null ? ul.SelectSingleNode("./li[@class='greyB2']").InnerText : string.Empty;
                        employee.cer_no = "";
                        employeeList.Add(employee);
                    }
                }
                _enterpriseInfo.employees = employeeList;
            }

            div = rootNode.SelectSingleNode("//div[@rel='layout-01_02_02']");
            if (div != null)
            {
                HtmlNode table = div.SelectSingleNode("./div/table[@class='dlPiece2']");
                HtmlNodeCollection trList = table.SelectNodes("./tr");
                int i = 1;
                List<Branch> employeeList = new List<Branch>();
                foreach (HtmlNode rowNode in trList)
                {
                    HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                    if (tdList == null) continue;
                    foreach (var td in tdList)
                    {
                        var ul = td.SelectSingleNode("./ul");
                        if (ul == null) continue;
                        Branch branch = new Branch();
                        branch.seq_no = i++;
                        branch.belong_org = ul.SelectSingleNode("./li[@class='padding3']") != null ? ul.SelectSingleNode("./li[@class='padding3']/span").NextSibling.InnerText : string.Empty;
                        branch.name = ul.SelectSingleNode("./li[@class='padding1']") != null ? ul.SelectSingleNode("./li[@class='padding1']").InnerText : string.Empty;
                        branch.oper_name = "";
                        branch.reg_no = ul.SelectSingleNode("./li[@class='padding2']") != null ? ul.SelectSingleNode("./li[@class='padding2']/span").NextSibling.InnerText : string.Empty;
                        employeeList.Add(branch);
                    }
                }
                _enterpriseInfo.branches = employeeList;
            }
            #endregion
        }
        private void LoadBasic_Only(EnterpriseInfo _enterpriseInfo, string responseData)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            #region 营业执照信息
            // 营业执照信息
            HtmlNode table = rootNode.SelectSingleNode("//table[@class='tableYyzz']");

            HtmlNodeCollection tdList = table.SelectNodes("./tr/td");
            for (int i = 0; i < tdList.Count; i++)
            {
                if (tdList[i].FirstChild == null)
                    continue;
                switch (Regex.Replace(tdList[i].FirstChild.InnerText.Trim().TrimStart('·').TrimEnd(':', '：'), "\\s", "").Replace("&nbsp;", ""))
                {
                    case "注册号":
                        _enterpriseInfo.reg_no = tdList[i].SelectSingleNode("./i").InnerText.Trim().Replace("&nbsp;", "");
                        break;
                    case "统一社会信用代码":
                        _enterpriseInfo.credit_no = tdList[i].SelectSingleNode("./i").InnerText.Trim().Replace("&nbsp;", "");
                        break;
                    case "注册号/统一社会信用代码":
                    case "统一社会信用代码/注册号":
                        if (tdList[i].SelectSingleNode("./i").InnerText.Trim().Replace("&nbsp;", "").Length == 18)
                            _enterpriseInfo.credit_no = tdList[i].SelectSingleNode("./i").InnerText.Trim().Replace("&nbsp;", "");
                        else
                            _enterpriseInfo.reg_no = tdList[i].SelectSingleNode("./i").InnerText.Trim().Replace("&nbsp;", "");
                        break;
                    case "企业名称":
                    case "名称":
                        _enterpriseInfo.name = tdList[i].SelectSingleNode("./i").InnerText.Trim().Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                        break;
                    case "类型":
                        _enterpriseInfo.econ_kind = tdList[i].SelectSingleNode("./i").InnerText.Trim().Replace("null", "").Replace("NULL", "");
                        break;
                    case "法定代表人":
                    case "负责人":
                    case "股东":
                    case "经营者":
                    case "执行事务合伙人":
                    case "投资人":
                        _enterpriseInfo.oper_name = tdList[i].SelectSingleNode("./i").InnerText.Trim().Replace("null", "").Replace("NULL", "");
                        break;
                    case "住所":
                    case "经营场所":
                    case "营业场所":
                    case "主要经营场所":
                        Address address = new Address();
                        address.name = "注册地址";
                        address.address = tdList[i].SelectSingleNode("./i").InnerText.Trim();
                        address.postcode = "";
                        _enterpriseInfo.addresses.Add(address);
                        break;
                    case "注册资金":
                    case "注册资本":
                    case "成员出资总额":
                        _enterpriseInfo.regist_capi = tdList[i].SelectSingleNode("./i").InnerText.Trim();
                        break;
                    case "成立日期":
                    case "登记日期":
                    case "注册日期":
                        _enterpriseInfo.start_date = tdList[i].SelectSingleNode("./i").InnerText.Trim();
                        break;
                    case "营业期限自":
                    case "经营期限自":
                    case "合伙期限自":
                        _enterpriseInfo.term_start = tdList[i].SelectSingleNode("./i").InnerText.Trim();
                        break;
                    case "营业期限至":
                    case "经营期限至":
                    case "合伙期限至":
                        _enterpriseInfo.term_end = tdList[i].SelectSingleNode("./i").InnerText.Trim();
                        break;
                    case "经营范围":
                    case "业务范围":
                        _enterpriseInfo.scope = tdList[i].SelectSingleNode("./i").InnerText.Trim().Replace("null", "").Replace("NULL", "");
                        break;
                    case "登记机关":
                        _enterpriseInfo.belong_org = tdList[i].SelectSingleNode("./i").InnerText.Trim();
                        break;
                    case "核准日期":
                        _enterpriseInfo.check_date = tdList[i].SelectSingleNode("./i").InnerText.Trim();
                        break;
                    case "登记状态":
                        _enterpriseInfo.status = tdList[i].SelectSingleNode("./i").InnerText.Trim();
                        break;
                    case "吊销日期":
                    case "注销日期":
                        _enterpriseInfo.end_date = tdList[i].SelectSingleNode("./i").InnerText.Trim();
                        break;
                    case "组成形式":
                        _enterpriseInfo.type_desc = tdList[i].SelectSingleNode("./i").InnerText.Trim();
                        break;
                    default:
                        break;
                }
            }
            if (string.IsNullOrWhiteSpace(_enterpriseName))
            {
                _enterpriseName = _enterpriseInfo.name;
            }
            #endregion
        }
        private void LoadBasic(EnterpriseInfo _enterpriseInfo, HtmlNode rootNode)
        {
            // 基本信息、股东信息、变更信息
            List<ChangeRecord> changeRecordList = new List<ChangeRecord>();
            HtmlNode div = rootNode.SelectSingleNode("//div[@id='layout-01_02_99']");
            if (div != null)
            {
                HtmlNode table = div.SelectSingleNode("./div/table");
                if (div.SelectSingleNode("./div").InnerText.Contains("变更信息"))
                {
                    HtmlNodeCollection trList = table.SelectNodes("./tr");
                    int k = 1;
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tddList = rowNode.SelectNodes("./td");
                        ChangeRecord changeRecord = new ChangeRecord();
                        if (tddList != null && tddList.Count > 4)
                        {
                            changeRecord.change_item = tddList[1].InnerText;
                            changeRecord.before_content = tddList[2].InnerText;
                            changeRecord.after_content = tddList[3].InnerText;
                            changeRecord.change_date = tddList[4].InnerText;
                            changeRecord.seq_no = k++;
                            changeRecordList.Add(changeRecord);
                        }
                    }
                }
            }
            _enterpriseInfo.changerecords = changeRecordList;

            div = rootNode.SelectSingleNode("//div[@id='layout-01_01_02']");
            if (div != null)
            {
                var table = div.SelectSingleNode("./div/table");
                if (div.SelectSingleNode("./div").InnerText.Contains("发起人及出资信息")
                    || div.SelectSingleNode("./div").InnerText.Contains("股东及出资信息"))
                {
                    // 股东信息
                    HtmlNodeCollection trList = table.SelectNodes("./tr");
                    Dictionary<int, HtmlNode> dic = new Dictionary<int, HtmlNode>();
                    if (trList != null && trList.Any())
                    {
                        for (int i = 1; i < trList.Count(); i++)
                        {
                            dic.Add(i - 1, trList[i]);
                        }
                    }
                    if (dic.Any())
                    {
                        Parallel.ForEach(dic, new ParallelOptions { MaxDegreeOfParallelism = 1 }, item => this.LoadAndParsePartners_Parallel(item.Key, item.Value));
                        _enterpriseInfo.partners.Sort(new PartnerComparer());
                    }
                }
            }


            div = rootNode.SelectSingleNode("//div[@id='layout-01_02_05']");
            if (div != null)
            {
                var table = div.SelectSingleNode("./div/table");
                if (div.SelectSingleNode("./div").InnerText.Contains("主管部门（出资人）信息"))
                {
                    // 股东信息
                    HtmlNodeCollection trList = table.SelectNodes("./tr");

                    foreach (var tr in trList)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Count > 3)
                        {
                            Partner partner = new Partner();
                            partner.identify_no = tds[4].InnerText.Trim();
                            partner.identify_type = tds[3].InnerText.Trim();
                            var uuid = string.Empty;
                            partner.ex_id = uuid;
                            partner.seq_no = _enterpriseInfo.partners.Count + 1;
                            partner.stock_name = tds[2].InnerText.Trim();
                            partner.stock_percent = "";
                            partner.stock_type = tds[1].InnerText.Trim();
                            partner.should_capi_items = new List<ShouldCapiItem>();
                            partner.real_capi_items = new List<RealCapiItem>();

                            _enterpriseInfo.partners.Add(partner);
                        }
                    }
                }
            }
        }

        #region LoadAndParsePartners_Parallel
        void LoadAndParsePartners_Parallel(int seqno, HtmlNode rowNode)
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
                        uuid = tdList[4].SelectSingleNode("./a").Attributes["onclick"].Value.Replace("ajaxReqInvestor('", "").Replace("')", "");
                }
                partner.ex_id = uuid;
                partner.seq_no = seqno;
                partner.stock_name = tdList[1].InnerText.Trim();
                partner.stock_percent = "";
                partner.stock_type = tdList[2].InnerText.Trim();
                partner.should_capi_items = new List<ShouldCapiItem>();
                partner.real_capi_items = new List<RealCapiItem>();
                // 解析股东详情
                //try
                //{
                //    if (!string.IsNullOrWhiteSpace(uuid))
                //    {
                //        var request = CreateRequest();
                //        request.AddOrUpdateRequestParameter("investorId", uuid);
                //        List<ResponseInfo> reponseList = request.GetResponseInfo(_requestXml.GetRequestListByName("investor_detials"));
                //        if (reponseList.Count() > 0)
                //        {
                 //          LoadAndParseInvestorDetails(partner, reponseList[0].Data);
                //        }
                //    }
                //}
                //catch (Exception ex)
                //{
                //    if (ex.Message == "TryHttpGetBySwitchProxy 超过最大次数！")
                //    {
                //        LogHelper.Info("年报详情在工商网站上不可访问,跳过该年报详情。");
                //    }
                //    else throw ex;
                //}
                _enterpriseInfo.partners.Add(partner);
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
            var tables = rootNode.SelectNodes("//table[@class='tableG3']");
            foreach (var table in tables)
            {
                var trs = table.SelectNodes("./tr");
                if (table.PreviousSibling.PreviousSibling.InnerText.Contains("认缴明细信息"))
                {
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Count > 2)
                        {
                            ShouldCapiItem item = new ShouldCapiItem();
                            item.invest_type = tds[0].InnerText;
                            item.shoud_capi = tds[1].InnerText;
                            item.should_capi_date = tds[2].InnerText;
                            partner.should_capi_items.Add(item);
                        }
                    }
                }
                else if (table.PreviousSibling.PreviousSibling.InnerText.Contains("实缴明细信息"))
                {
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Count > 2)
                        {
                            RealCapiItem item = new RealCapiItem();
                            item.invest_type = tds[0].InnerText;
                            item.real_capi = tds[1].InnerText;
                            item.real_capi_date = tds[2].InnerText;
                            partner.real_capi_items.Add(item);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析动产抵押登记详情
        /// <summary>
        /// 解析动产抵押登记详情
        /// </summary>
        /// <param name="mortgage"></param>
        /// <param name="responseData"></param>
        private void LoadAndParseMortgageDetails(MortgageInfo mortgage, String responseData)
        {
            if (string.IsNullOrEmpty(responseData)) return;
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);

            HtmlNode rootNode = document.DocumentNode;
            HtmlNodeCollection infoTable = rootNode.SelectNodes("//table");
            if (infoTable != null)
            {
                foreach (HtmlNode table in infoTable)
                {
                    if (table.PreviousSibling == null || table.PreviousSibling.PreviousSibling == null)
                        continue;
                    string header = table.PreviousSibling.PreviousSibling.InnerText;
                    HtmlNodeCollection trList = table.SelectNodes("./tr");
                    if (header.Contains("被担保债权概况"))
                    {
                        if (trList != null && trList.Count > 1)
                        {
                            for (int i = 0; i < trList.Count; i++)
                            {
                                HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                                HtmlNodeCollection thList = trList[i].SelectNodes("./th");
                                for (int j = 0; j < tdList.Count; j++)
                                {
                                    switch (thList[j].InnerText.Trim())
                                    {
                                        case "种类":
                                            mortgage.debit_type = tdList[j].InnerText.Trim();
                                            break;
                                        case "数额":
                                            mortgage.debit_amount = tdList[j].InnerText.Trim();
                                            break;
                                        case "担保的范围":
                                            mortgage.debit_scope = tdList[j].InnerText.Trim();
                                            break;
                                        case "债务人履行债务的期限":
                                            mortgage.debit_period = tdList[j].InnerText.Trim();
                                            break;
                                        case "备注":
                                            mortgage.debit_remarks = tdList[j].InnerText.Trim();
                                            break;
                                    }
                                }
                            }
                        }
                    }
                    else if (header.Contains("抵押权人概况"))
                    {
                        List<Mortgagee> mortgagees = new List<Mortgagee>();
                        if (trList != null && trList.Count > 1)
                        {
                            for (int i = 1; i < trList.Count; i++)
                            {
                                Mortgagee mortgagee = new Mortgagee();
                                HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                                if (tdList != null && tdList.Count > 3)
                                {
                                    mortgagee.seq_no = mortgagees.Count + 1;
                                    mortgagee.name = tdList[1].InnerText.Trim();
                                    mortgagee.identify_type = tdList[2].InnerText.Trim();
                                    mortgagee.identify_no = tdList[3].InnerText.Trim();
                                    mortgagees.Add(mortgagee);
                                }
                            }

                        }
                        mortgage.mortgagees = mortgagees;
                    }
                    else if (header.Contains("抵押物概况信息"))
                    {
                        List<Guarantee> guarantees = new List<Guarantee>();
                        if (trList != null && trList.Count > 0)
                        {
                            for (int i = 1; i < trList.Count; i++)
                            {
                                Guarantee guarantee = new Guarantee();
                                HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                                if (tdList != null && tdList.Count > 4)
                                {
                                    guarantee.seq_no = guarantees.Count + 1;
                                    guarantee.name = tdList[1].InnerText.Trim();
                                    guarantee.belong_to = tdList[2].InnerText.Trim();
                                    guarantee.desc = tdList[3].InnerText.Trim();
                                    guarantee.remarks = tdList[4].InnerText.Trim();
                                    guarantees.Add(guarantee);
                                }
                            }

                        }
                        mortgage.guarantees = guarantees;
                    }
                }
            }
        }
        #endregion

        #region 解析行政处罚信息详情
        /// <summary>
        /// 解析行政处罚信息详情
        /// </summary>
        /// <param name="mortgage"></param>
        /// <param name="responseData"></param>
        private void LoadAndParsePunishDetails(AdministrativePunishment punish, String responseData)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);

            HtmlNode rootNode = document.DocumentNode;
            var div = rootNode.SelectSingleNode("//div[@class='detail-info']");
            HtmlNodeCollection infoTable = div.SelectNodes("./div/table");
            if (infoTable != null)
            {
                foreach (HtmlNode table in infoTable)
                {
                    HtmlNodeCollection trList = table.SelectNodes("./tr");
                    if (trList != null && trList.Count > 2)
                    {
                        for (int i = 0; i < trList.Count; i++)
                        {
                            HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                            HtmlNodeCollection thList = trList[i].SelectNodes("./td/i");
                            if (tdList != null && thList != null && tdList.Count == thList.Count)
                            {
                                for (int j = 0; j < tdList.Count; j++)
                                {
                                    string str = tdList[j].InnerText.Trim().Replace(thList[j].InnerText.Trim(), "").Replace("·", "").Replace("&nbsp;", "").Replace("：", "");
                                    switch (str)
                                    {
                                        case "名称":
                                            punish.name = thList[j].InnerText.Trim();
                                            break;
                                        case "统一社会信用代码/注册号":
                                            if (thList[j].InnerText.Trim().Length == 15)
                                                punish.reg_no = thList[j].InnerText.Trim();

                                            break;
                                        case "法定代表人（负责人）姓名":
                                            punish.oper_name = thList[j].InnerText.Trim();
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

        #region 解析tab2
        /// <summary>
        /// 解析tab2
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseReport(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            LoadFinicalConReport(_enterpriseInfo, rootNode);
            Parallel.Invoke(() =>
            {
                  LoadBasicAnnualReport(_enterpriseInfo, rootNode);
            },
            () =>
            {
                  LoadLicenseReport(_enterpriseInfo, rootNode);
            },
            () =>
            {
                 LoadKnowledgePropertyReport(_enterpriseInfo, rootNode);
            },
            () =>
            {
                LoadStockReport(_enterpriseInfo, rootNode);
            },
            () =>
            {
                LoadFinicalConReport(_enterpriseInfo, rootNode);
            }         
            
            );
        }     

        private void LoadFinicalConReport(EnterpriseInfo _enterpriseInfo, HtmlNode rootNode)
        {
            #region 股东及出资信息
            var tables = rootNode.SelectNodes("//div[@rel='layout-02_04_01']/div/table");
            if (tables != null && tables.Count > 0)
            {
                var str = rootNode.InnerText.Trim();              
                if (!string.IsNullOrEmpty(str))
                {
                    List<FinancialContribution> financialcontributions = new List<FinancialContribution>();
                    var lists = str.Split(new string[] { "list.push(investor)" }, StringSplitOptions.RemoveEmptyEntries);
                    if (lists != null && lists.Length > 0)
                    {
                        for (int i = 0; i < lists.Length - 1; i++)
                        {
                            FinancialContribution financialcontribution = new FinancialContribution();
                            financialcontribution.seq_no = financialcontributions.Count + 1;

                            var tt = lists[i].Split(new string[] { "inv =" }, StringSplitOptions.RemoveEmptyEntries);
                            if (tt != null)
                                financialcontribution.investor_name = tt[1].Split(new string[] { "\"", "\";" }, StringSplitOptions.RemoveEmptyEntries)[1];
                            financialcontribution.investor_type = "";
                            var res = lists[i].Split(new string[] { "var entOthInvtSet = new Array();", "var entOthInvtactlSet = new Array();" }, StringSplitOptions.RemoveEmptyEntries);//认缴
                            double total_should_capi = 0;
                            double total_real_capi = 0;
                            if (res != null && res.Length > 2)
                            {
                                var renjiaoList = res[1].Split(new string[] { "entOthInvtSet.push(invt);" }, StringSplitOptions.RemoveEmptyEntries);
                                for (int j = 0; j < renjiaoList.Length - 1; j++)
                                {
                                    FinancialContribution.ShouldCapiItem item = new FinancialContribution.ShouldCapiItem();
                                    item.should_capi = renjiaoList[j].Split(new string[] { "subConAm =" }, StringSplitOptions.RemoveEmptyEntries)[1].Split(new string[] { "\"", "\";" }, StringSplitOptions.RemoveEmptyEntries)[1];
                                    double should_capi = 0;
                                    total_should_capi += double.TryParse(item.should_capi, out should_capi) ? should_capi : 0;
                                    var type = renjiaoList[j].Split(new string[] { "conForm =" }, StringSplitOptions.RemoveEmptyEntries)[1].Split(new string[] { "\"", "\";" }, StringSplitOptions.RemoveEmptyEntries)[1];
                                    item.should_invest_type = GetFinacialType(type);
                                    item.should_invest_date = renjiaoList[j].Split(new string[] { "conDate =" }, StringSplitOptions.RemoveEmptyEntries)[1].Split(new string[] { "'", "';" }, StringSplitOptions.RemoveEmptyEntries)[1];
                                    financialcontribution.should_capi_items.Add(item);
                                }

                                var shijiaoList = res[2].Split(new string[] { "entOthInvtactlSet.push(invtActl);" }, StringSplitOptions.RemoveEmptyEntries);//实缴
                                for (int j = 0; j < shijiaoList.Length - 1; j++)
                                {
                                    FinancialContribution.RealCapiItem item = new FinancialContribution.RealCapiItem();
                                    item.real_capi = shijiaoList[j].Split(new string[] { "acConAm =" }, StringSplitOptions.RemoveEmptyEntries)[1].Split(new string[] { "\"", "\";" }, StringSplitOptions.RemoveEmptyEntries)[1];
                                    double real_capi = 0;
                                    total_real_capi += double.TryParse(item.real_capi, out real_capi) ? real_capi : 0;
                                    var type = shijiaoList[j].Split(new string[] { "conForm =" }, StringSplitOptions.RemoveEmptyEntries)[1].Split(new string[] { "\"", "\";" }, StringSplitOptions.RemoveEmptyEntries)[1];
                                    item.real_invest_type = GetFinacialType(type);
                                    item.real_invest_date = shijiaoList[j].Split(new string[] { "conDate =" }, StringSplitOptions.RemoveEmptyEntries)[1].Split(new string[] { "'", "';" }, StringSplitOptions.RemoveEmptyEntries)[1];
                                    financialcontribution.real_capi_items.Add(item);
                                }
                            }
                            financialcontribution.total_real_capi = total_real_capi.ToString();
                            financialcontribution.total_should_capi = total_should_capi.ToString();
                            financialcontributions.Add(financialcontribution);
                        }
                    }
                    _enterpriseInfo.financial_contributions = financialcontributions;
                }
            }
            #endregion
        }

        private void LoadStockReport(EnterpriseInfo _enterpriseInfo, HtmlNode rootNode)
        {
            #region 股权变更信息
            HtmlNode div = rootNode.SelectSingleNode("//div[@rel='layout-02_06_01']");
            if (div != null)
            {
                HtmlNodeCollection trList = div.SelectNodes("./div/table/tr");
                if (trList != null && trList.Count > 0)
                {
                    List<StockChangeItem> stockchanges = new List<StockChangeItem>();
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                        if (tdList != null && tdList.Count > 4)
                        {
                            StockChangeItem stockchangeitem = new StockChangeItem();
                            stockchangeitem.seq_no = stockchanges.Count + 1;
                            stockchangeitem.name = tdList[1].InnerText.Trim();

                            stockchangeitem.before_percent = tdList[2].InnerText.Trim();
                            stockchangeitem.after_percent = tdList[3].InnerText.Trim();
                            stockchangeitem.change_date = tdList[4].InnerText.Trim();
                            stockchanges.Add(stockchangeitem);
                        }
                    }
                    _enterpriseInfo.stock_changes = stockchanges;
                }
            }
            #endregion
        }

        private void LoadKnowledgePropertyReport(EnterpriseInfo _enterpriseInfo, HtmlNode rootNode)
        {
            #region 知识产权出质登记信息
            HtmlNode div = rootNode.SelectSingleNode("//div[@rel='layout-02_03_01']");
            if (div != null)
            {
                HtmlNodeCollection trList = div.SelectNodes("./div/table/tr");
                if (trList != null && trList.Count > 0)
                {
                    List<KnowledgeProperty> knowledgepropertyList = new List<KnowledgeProperty>();
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                        if (tdList != null && tdList.Count > 8)
                        {
                            KnowledgeProperty knowledgeproperty = new KnowledgeProperty();
                            knowledgeproperty.seq_no = knowledgepropertyList.Count + 1;
                            knowledgeproperty.number = tdList[2].InnerText.Trim();
                            knowledgeproperty.name = tdList[3].InnerText.Trim();
                            knowledgeproperty.type = tdList[4].InnerText.Trim();
                            knowledgeproperty.pledgor = tdList[5].InnerText;
                            knowledgeproperty.pawnee = tdList[6].InnerText;
                            knowledgeproperty.period = tdList[7].InnerText;
                            knowledgeproperty.status = tdList[8].InnerText;
                            // 加载知识产权出质登记详细信息
                            //HtmlNode aNode = tdList[8].SelectSingleNode("./a");
                            //if (aNode != null)
                            //{
                            //    string reportHerf = aNode.Attributes["href"].Value;
                            //    string uuid = Regex.Split(reportHerf, "uuid=")[1];
                            //    _request.AddOrUpdateRequestParameter("license_uuid", uuid);
                            //    List<ResponseInfo> responseList = GetResponseInfo(_requestXml.GetRequestListByName("licenseDetail"));
                            //    if (responseList != null && responseList.Count > 0)
                            //    {
                            //        //LoadAndParseReportDetail(responseList[0].Data, report);
                            //    }
                            //}
                            knowledgepropertyList.Add(knowledgeproperty);
                        }
                    }
                    _enterpriseInfo.knowledge_properties = knowledgepropertyList;
                }
            }
            #endregion
        }

        private void LoadLicenseReport(EnterpriseInfo _enterpriseInfo, HtmlNode rootNode)
        {
            #region 行政许可信息
            HtmlNode div = rootNode.SelectSingleNode("//div[@rel='layout-02_02_01']");
            if (div != null)
            {
                HtmlNodeCollection trList = div.SelectNodes("./div/table/tr");
                if (trList != null && trList.Count > 0)
                {
                    List<LicenseInfo> licenseList = new List<LicenseInfo>();
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                        if (tdList != null && tdList.Count > 7)
                        {
                            LicenseInfo licenseinfo = new LicenseInfo();
                            licenseinfo.seq_no = licenseList.Count + 1;
                            licenseinfo.number = tdList[1].InnerText.Trim();

                            licenseinfo.name = tdList[2].InnerText.Trim();
                            licenseinfo.start_date = tdList[3].InnerText.Trim();
                            licenseinfo.end_date = tdList[4].InnerText.Trim();
                            licenseinfo.department = tdList[5].InnerText.Trim();
                            licenseinfo.content = tdList[6].InnerText.Trim();
                            licenseinfo.status = tdList[7].InnerText;
                            // 加载行政许可详细信息
                            //HtmlNode aNode = tdList[8].SelectSingleNode("./a");
                            //if (aNode != null)
                            //{
                            //    string reportHerf = aNode.Attributes["href"].Value;
                            //    string uuid = Regex.Split(reportHerf, "uuid=")[1];
                            //    _request.AddOrUpdateRequestParameter("license_uuid", uuid);
                            //    List<ResponseInfo> responseList = GetResponseInfo(_requestXml.GetRequestListByName("licenseDetail"));
                            //    if (responseList != null && responseList.Count > 0)
                            //    {
                            //        //LoadAndParseReportDetail(responseList[0].Data, report);
                            //    }
                            //}
                            licenseList.Add(licenseinfo);
                        }
                    }
                    _enterpriseInfo.licenses = licenseList;
                }
            }
            #endregion
        }

        private void LoadBasicAnnualReport(EnterpriseInfo _enterpriseInfo, HtmlNode rootNode)
        {
            try
            {
                #region 解析年报
                HtmlNode div = rootNode.SelectSingleNode("//div[@rel='layout-02_01_01']");
                if (div != null)
                {
                    HtmlNodeCollection trList = div.SelectNodes("./div/table/tr");
                    if (trList != null && trList.Count > 1)
                    {
                        trList.Remove(0);
                        List<Report> reportList = new List<Report>();
                        Parallel.ForEach(trList, new ParallelOptions(){ MaxDegreeOfParallelism=1}, rowNode => this.LoadBasicAnnualReport_Parallel(rowNode, reportList));
                        reportList.Sort(new ReportComparer());
                        _enterpriseInfo.reports = reportList;

                    }
                }
                #endregion
            }
            catch (Exception ex)
            {
                _enterpriseInfo.reports.Clear();
                Console.WriteLine("Exception when LoadAndParseReport.." + ex.ToString());
                LogHelper.Error("Exception when LoadAndParseReport.." + ex.ToString());
            }
        }
        #endregion

        #region LoadBasicAnnualReport
        void LoadBasicAnnualReport_Parallel(HtmlNode rowNode, List<Report> reportList)
        {
            HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
            if (tdList != null && tdList.Count > 2)
            {
                Report report = new Report();
                report.report_name = tdList[1].InnerText.Trim();
                report.report_year = tdList[1].InnerText.Trim().Length > 4 ? tdList[1].InnerText.Trim().Substring(0, 4) : "";
                report.report_date = tdList[2].InnerText;
                if (!reportsNeedToLoad.Any() || reportsNeedToLoad.Contains(report.report_year))
                {
                    // 加载解析年报详细信息
                    HtmlNode aNode = tdList[3].SelectSingleNode("./a");
                    if (aNode != null)
                    {
                        string reportHerf = aNode.Attributes["href"].Value;
                        string reportId = Regex.Split(reportHerf, "uuid=")[1];
                        var request = CreateRequest();
                        request.AddOrUpdateRequestParameter("reportId", reportId);
                        List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("reportDetail"));
                        if (responseList != null && responseList.Count > 0)
                        {
                            LoadAndParseReportDetail(responseList[0].Data, report);
                        }
                        reportList.Add(report);
                    }
                }
            }
        }
        #endregion

        #region 出资类型转换
        /// <summary>
        /// 出资类型转换
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        private string GetFinacialType(string code)
        {
            var result = "";
            var rangeList = new Dictionary<string, string>();
            rangeList.Add("1", "货币");
            rangeList.Add("2", "实物");
            rangeList.Add("3", "知识产权");
            rangeList.Add("4", "债权");
            rangeList.Add("5", "高新技术成果");
            rangeList.Add("6", "土地使用权");
            rangeList.Add("7", "股权");
            rangeList.Add("8", "劳务");
            rangeList.Add("9", "其他");
            var rangeArr = code.Split(',');
            for (int i = 0; i < rangeArr.Length; i++)
            {
                if (rangeList.Keys.Contains(rangeArr[i]))
                {
                    if (i == rangeArr.Length - 1)
                        result += rangeList[rangeArr[i]];
                    else
                        result += rangeList[rangeArr[i]] + ",";
                }
            }
            return result;
        }
        #endregion
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

            HtmlNodeCollection tables = rootNode.SelectNodes("//div[@class='content1']");
            foreach (HtmlNode table in tables)
            {
                string title = table.SelectSingleNode("./div[@class='titleTop']").InnerText.Trim();
                var temp = table.SelectSingleNode("./div[@class='titleTop']");
                if (title == "基本信息")
                {
                    // 企业基本信息
                    HtmlNodeCollection trList = table.SelectNodes("./table/tr");
                    foreach (HtmlNode rowNode in trList)
                    {
                       // HtmlNodeCollection thList = rowNode.SelectNodes("./th");
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                       // HtmlNodeCollection thList = rowNode.SelectNodes("./td/i");
                       if (tdList != null &&  tdList.Count>0)
                        //if (thList != null && tdList != null && thList.Count == tdList.Count)
                        {
                            for (int i = 0; i < tdList.Count; i++)
                            {
                                var th = tdList[i].SelectSingleNode("./i");
                                if(th == null)
                                {
                                    th = tdList[i].SelectSingleNode("./span/i");
                                }
                                if (th != null)
                                {
                                    string str = tdList[i].InnerText.Trim().Replace("&nbsp;", "")
                                            .Replace("•", "").Replace("·", "").Replace("\t", "").Replace("\r", "").Replace("\n", "").Replace(" ", "");
                                    if (!string.IsNullOrEmpty(th.InnerText.Trim()))
                                    {
                                        str = str.Split('：').First();
                                    }
                                    switch (str)
                                    {
                                        case "营业执照注册号":
                                        case "注册号":
                                            report.reg_no = th.InnerText.Trim().Replace("&nbsp;", "");
                                            break;
                                        case "统一社会信用代码":
                                            report.credit_no = th.InnerText.Trim().Replace("&nbsp;", "");
                                            break;
                                        case "注册号/统一社会信用代码":
                                        case "统一社会信用代码/注册号":
                                            if (th.InnerText.Trim().Replace("&nbsp;", "").Length == 18)
                                                report.credit_no = th.InnerText.Trim().Replace("&nbsp;", "");
                                            else
                                                report.reg_no = th.InnerText.Trim().Replace("&nbsp;", "");
                                            break;
                                        case "名称":
                                        case "企业名称":
                                            report.name = th.InnerText.Trim().Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                                            break;
                                        case "联系电话":
                                        case "企业联系电话":
                                            report.telephone = th.InnerText.Trim();
                                            break;
                                        case "企业通信地址":
                                            report.address = th.InnerText.Trim();
                                            break;
                                        case "邮政编码":
                                            report.zip_code = th.InnerText.Trim();
                                            break;
                                        case "企业电子邮箱":
                                            report.email = th.InnerText.Trim();
                                            break;
                                        case "企业是否有投资信息或购买其他公司股权":
                                        case "企业是否有对外投资设立企业信息":
                                            report.if_invest = th.InnerText.Trim();
                                            break;
                                        case "是否有网站或网店":
                                        case "是否有网站或网点":
                                            report.if_website = th.InnerText.Trim();
                                            break;
                                        case "企业经营状态":
                                            report.status = th.InnerText.Trim();
                                            break;
                                        case "从业人数":
                                            report.collegues_num = th.InnerText.Trim();
                                            break;
                                        case "有限责任公司本年度是否发生股东股权转让":
                                            report.if_equity = th.InnerText.Trim();
                                            break;
                                        case "经营者姓名":
                                            report.oper_name = th.InnerText.Trim();
                                            break;
                                        case "资金数额":
                                            report.total_equity = th.InnerText.Trim(); ;
                                            break;
                                        default:
                                            break;
                                    }
                                }
                            }
                        }
                    }
                }
                else if (title == "网站或网店信息")
                {
                    HtmlNodeCollection trList = table.SelectNodes("./div");

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
                    HtmlNodeCollection trList = table.SelectNodes("./table/tr");

                    List<Partner> partnerList = new List<Partner>();
                    int j = 1;
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                        if (tdList != null && tdList.Count > 7)
                        {
                            Partner item = new Partner();

                            item.seq_no = j++;
                            item.stock_name = tdList[1].InnerText;
                            item.stock_type = tdList[7].InnerText;
                            item.identify_no = "";
                            item.identify_type = "";
                            item.stock_percent = "";
                            item.ex_id = "";
                            item.real_capi_items = new List<RealCapiItem>();
                            item.should_capi_items = new List<ShouldCapiItem>();

                            ShouldCapiItem sItem = new ShouldCapiItem();
                            var shoudCapi = tdList[2].InnerText.Trim();
                            sItem.shoud_capi = string.IsNullOrEmpty(shoudCapi) ? "" : shoudCapi;
                            sItem.should_capi_date = tdList[3].InnerText.Trim();
                            sItem.invest_type = tdList[4].InnerText.Trim();
                            item.should_capi_items.Add(sItem);

                            RealCapiItem rItem = new RealCapiItem();
                            var realCapi = tdList[5].InnerText.Trim();
                            rItem.real_capi = string.IsNullOrEmpty(realCapi) ? "" : realCapi;
                            rItem.real_capi_date = tdList[6].InnerText.Trim();
                            rItem.invest_type = tdList[7].InnerText.Trim();
                            item.real_capi_items.Add(rItem);

                            partnerList.Add(item);
                        }
                    }
                    report.partners = partnerList;
                }
                else if (title == "对外投资信息")
                {
                    HtmlNodeCollection trList = table.SelectNodes("./table/tr");

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
                #region 股权变更信息
                else if (title.Contains("股权变更信息"))
                {
                    HtmlNodeCollection trList = table.SelectNodes("./table/tr");
                    List<StockChangeItem> stockchanges = new List<StockChangeItem>();
                    for (int i = 0; i < trList.Count; i++)
                    {
                        HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                        if (tdList != null && tdList.Count > 3)
                        {
                            StockChangeItem stockchangeitem = new StockChangeItem();
                            stockchangeitem.seq_no = stockchanges.Count + 1;
                            stockchangeitem.name = tdList[1].InnerText.Trim();

                            stockchangeitem.before_percent = tdList[2].InnerText.Trim();
                            stockchangeitem.after_percent = tdList[3].InnerText.Trim();
                            stockchangeitem.change_date = tdList[4].InnerText.Trim();
                            stockchanges.Add(stockchangeitem);
                        }
                    }
                    report.stock_changes = stockchanges;
                }
                #endregion
                else if (title.Contains("修改记录") || title.Contains("修改信息"))
                {
                    HtmlNodeCollection trList = table.SelectNodes("./table/tr");
                    List<UpdateRecord> changes = new List<UpdateRecord>();
                    for (int i = 0; i < trList.Count; i++)
                    {
                        HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                        if (tdList != null && tdList.Count > 4 && trList[i].InnerText.Contains("股东"))
                        {
                            UpdateRecord record = new UpdateRecord();
                            record.seq_no = changes.Count + 1;
                            record.update_item = tdList[1].InnerText.Trim();
                            record.before_update = tdList[2].InnerText.Trim();
                            record.after_update = tdList[3].InnerText.Trim();
                            record.update_date = tdList[4].InnerText.Trim();
                            changes.Add(record);
                        }
                    }
                    report.update_records = changes;
                }
                #region 对外担保信息
                else if (title.Contains("对外提供保证担保信息"))
                {
                    HtmlNodeCollection trList = table.SelectNodes("./table/tr");
                    List<ExternalGuarantee> externalguarantees = new List<ExternalGuarantee>();
                    for (int i = 0; i < trList.Count; i++)
                    {
                        HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                        if (tdList != null && tdList.Count > 6)
                        {
                            ExternalGuarantee externalguarantee = new ExternalGuarantee();
                            externalguarantee.seq_no =i;
                            externalguarantee.creditor = tdList[1].InnerText.Trim();
                            externalguarantee.type = tdList[2].InnerText.Trim();
                            externalguarantee.amount = tdList[3].InnerText.Trim();
                            externalguarantee.period = tdList[4].InnerText.Trim();
                            externalguarantee.guarantee_time = tdList[5].InnerText.Trim();
                            externalguarantee.guarantee_type = tdList[6].InnerText.Trim();
                            externalguarantees.Add(externalguarantee);
                        }
                    }
                    report.external_guarantees = externalguarantees;
                }
                #endregion
                else if (title == "企业资产状况信息" || title.StartsWith("生产经营情况"))
                {
                    HtmlNodeCollection trList = table.SelectNodes("./table/tr");

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
                                    case "销售额或营业收入":
                                    case "销售总额":
                                    case "营业总收入":
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

        //private void LoadAndParseFreeze(string responseData, EnterpriseInfo _enterpriseInfo)
        //{
        //    HtmlDocument document = new HtmlDocument();
        //    document.LoadHtml(responseData);
        //    HtmlNode rootNode = document.DocumentNode;

        //    var tables = rootNode.SelectNodes("//table");
        //    if (tables != null)
        //    {
        //        foreach (var table in tables)
        //        {
        //            string title = table.SelectNodes("./tr/th")[0].InnerText.Trim();
        //            if (title == "司法股权冻结信息")
        //            {
        //                HtmlNodeCollection trList = table.SelectNodes("./tr");

        //                List<JudicialFreeze> freezes = new List<JudicialFreeze>();
        //                int j = 1;
        //                foreach (HtmlNode rowNode in trList)
        //                {
        //                    HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
        //                    if (tdList != null && tdList.Count > 2)
        //                    {
        //                        JudicialFreeze item = new JudicialFreeze();

        //                        item.seq_no = j++;
        //                        item.be_executed_person = tdList[1].InnerText;
        //                        item.amount = tdList[2].InnerText;
        //                        item.executive_court = tdList[3].InnerText;
        //                        item.number = tdList[4].InnerText;
        //                        item.status = tdList[5].InnerText;

        //                        var match = Regex.Match(tdList[6].InnerHtml, "href=\"(.*?)\"");
        //                        if (match != null && !string.IsNullOrEmpty(match.Value))
        //                        {
        //                            string id = match.Value;
        //                            id = id.Replace("href=\"", "").Replace("\"", "");
        //                            var response = this._request.RequestData(new RequestSetting("get", id, string.Empty, string.Empty, "FreezeDetail"));
        //                            HtmlDocument docDetail = new HtmlDocument();
        //                            docDetail.LoadHtml(response.Data);
        //                            HtmlNode root = docDetail.DocumentNode;
        //                            var tableDetail = root.SelectSingleNode("//div[@class='detail-info']/table");
        //                            if (tableDetail != null)
        //                            {
        //                                var trDetail = tableDetail.SelectNodes("./tr");
        //                                if (trDetail != null)
        //                                {
        //                                    LoadAndParseFreezeDetail(item, trDetail);
        //                                }
        //                            }

        //                        }

        //                        freezes.Add(item);
        //                    }
        //                }
        //                this._enterpriseInfo.judicial_freezes = freezes;
        //            }
        //        }
        //    }
        //}

        private void LoadAndParseFreezeDetail(JudicialFreeze item, HtmlNodeCollection trList)
        {
            if (trList != null && trList.Count > 1)
            {
                JudicialFreezeDetail freeze = new JudicialFreezeDetail();
                for (int i = 1; i < trList.Count; i++)
                {
                    HtmlNodeCollection thList = trList[i].SelectNodes("./th");
                    HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                    if (thList != null && tdList != null && thList.Count == tdList.Count)
                    {
                        for (int j = 0; j < thList.Count; j++)
                        {
                            switch (thList[j].InnerText.Trim())
                            {
                                case "执行法院":
                                    freeze.execute_court = tdList[j].InnerText.Trim();
                                    break;
                                case "执行事项":
                                    freeze.assist_item = tdList[j].InnerText.Trim();
                                    break;
                                case "执行裁定书文号":
                                    freeze.adjudicate_no = tdList[j].InnerText.Trim();
                                    break;
                                case "执行通知书文号":
                                    freeze.notice_no = tdList[j].InnerText.Trim();
                                    break;
                                case "被执行人":
                                    freeze.assist_name = tdList[j].InnerText.Trim();
                                    break;
                                case "被执行人持有股份、其他投资权益的数额":
                                    freeze.freeze_amount = tdList[j].InnerText.Trim();
                                    break;
                                case "被执行人证件种类":
                                    freeze.assist_ident_type = tdList[j].InnerText.Trim();
                                    break;
                                case "被执行人证件号码":
                                    freeze.assist_ident_no = tdList[j].InnerText.Trim();
                                    break;
                                case "冻结期限自":
                                    freeze.freeze_start_date = tdList[j].InnerText.Trim();
                                    break;
                                case "冻结期限至":
                                    freeze.freeze_end_date = tdList[j].InnerText.Trim();
                                    break;
                                case "冻结期限":
                                    freeze.freeze_year_month = tdList[j].InnerText.Trim();
                                    break;
                                case "公示日期":
                                    freeze.public_date = tdList[j].InnerText.Trim();
                                    break;
                            }
                        }
                    }
                }
                item.detail = freeze;
            }
        }


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
        void CheckMessageIsError(HtmlNode rootNode)
        {
            var h2 = rootNode.SelectSingleNode("//div[@class='notice']");
            if (h2 != null)
            {
                if (!h2.InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace("&nbsp;", "").Replace(" ", "").Contains(_enterpriseName))
                {
                    throw new Exception("云南网站内容错乱");
                }
            }
        }
    }
}
