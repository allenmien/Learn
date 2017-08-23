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
using MongoDB.Bson;

namespace iOubo.iSpider.Infrastructure.Parser
{
    public class ConverterSX: IConverter
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
        string bodyid = string.Empty;
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
            List<ResponseInfo> responseList = _request.GetResponseInfo(_requestXml.GetRequestListByGroup("basic"));
            var basicInfo = responseList.FirstOrDefault(p => p.Name == "gongshang");
            var qiyexinxi = responseList.FirstOrDefault(p => p.Name == "qiyejishixinxi");
            if (qiyexinxi != null)
            {
                var match = Regex.Match(qiyexinxi.Data, "mainId=.*?,");
                if (match != null)
                {
                    bodyid = match.Value.Replace("mainId=", "").Replace("'", "").Replace(",", "").Trim();
                }
            }
            this.LoadAndParseBasic(basicInfo.Data, _enterpriseInfo);
            this.LoadAndParseTab01(basicInfo.Data);

            SummaryEntity summaryEntity = new SummaryEntity();
            summaryEntity.Enterprise = _enterpriseInfo;
            summaryEntity.Abnormals = _abnormals;
            summaryEntity.Checkups = _checkups;

            return summaryEntity;
        }

        #region 初始化
        /// <summary>
        /// 初始化
        /// </summary>
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
        #endregion

        #region 解析基本信息
        /// <summary>
        /// 解析基本信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseBasic(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            if (string.IsNullOrWhiteSpace(responseData))
            {
                var request = this.CreateRequest();
                var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("head"));
                if (responseList != null && responseList.Any())
                {
                    this.LoadAndParseHeadInfo(responseList.First().Data);
                }
            }
            else
            {
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;
                // 基本信息
                HtmlNode table = rootNode.SelectSingleNode("//table[@class='xinxi']");
                if (table != null)
                {
                    HtmlNodeCollection tdList = table.SelectNodes("./tr/td");
                    var inner_tdList = table.SelectNodes("./tr/td/td");
                    if (inner_tdList != null && tdList.Any())
                    {
                        foreach (var inner_td in inner_tdList)
                        {
                            tdList.Add(inner_td);
                        }
                    }
                    for (int i = 0; i < tdList.Count; i++)
                    {
                        switch (tdList[i].InnerText.Split('：', ':')[0].Replace("&nbsp;", "").Replace("·", "").Trim())
                        {
                            case "注册号":
                                _enterpriseInfo.reg_no = tdList[i].SelectSingleNode("./span").InnerText.Trim().Replace("&nbsp;", "");
                                break;
                            case "统一社会信用代码":
                                _enterpriseInfo.credit_no = tdList[i].SelectSingleNode("./span").InnerText.Trim().Replace("&nbsp;", "");
                                break;
                            case "注册号/统一社会信用代码":
                            case "统一社会信用代码/注册号":
                                if (tdList[i].SelectSingleNode("./span").InnerText.Trim().Replace("&nbsp;", "").Length == 18)
                                    _enterpriseInfo.credit_no = tdList[i].SelectSingleNode("./span").InnerText.Trim().Replace("&nbsp;", "");
                                else
                                    _enterpriseInfo.reg_no = tdList[i].SelectSingleNode("./span").InnerText.Trim().Replace("&nbsp;", "");
                                break;
                            case "名称":
                            case "企业名称":
                                _enterpriseInfo.name = tdList[i].SelectSingleNode("./span").InnerText.Trim().Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                                break;
                            case "类型":
                                _enterpriseInfo.econ_kind = tdList[i].SelectSingleNode("./span").InnerText.Trim().Replace("null", "").Replace("NULL", "");
                                break;
                            case "法定代表人":
                            case "负责人":
                            case "股东":
                            case "经营者":
                            case "执行事务合伙人":
                            case "投资人":
                                _enterpriseInfo.oper_name = tdList[i].SelectSingleNode("./span").InnerText.Trim().Replace("null", "").Replace("NULL", "");
                                break;
                            case "住所":
                            case "经营场所":
                            case "营业场所":
                            case "主要经营场所":
                                Address address = new Address();
                                address.name = "注册地址";
                                address.address = tdList[i].SelectSingleNode("./span").InnerText.Trim();
                                address.postcode = "";
                                _enterpriseInfo.addresses.Add(address);
                                break;
                            case "注册资金":
                            case "注册资本":
                            case "成员出资总额":
                                if (tdList[i].SelectSingleNode("./td") == null)
                                {
                                    _enterpriseInfo.regist_capi = tdList[i].SelectSingleNode("./span").InnerText.Trim();
                                }
                                else
                                {
                                    var inner_td = tdList[i].SelectSingleNode("./td");
                                    tdList[i].RemoveChild(inner_td);
                                    _enterpriseInfo.regist_capi = tdList[i].SelectSingleNode("./span").InnerText.Trim();
                                }
                                break;
                            case "成立日期":
                            case "登记日期":
                            case "注册日期":
                                _enterpriseInfo.start_date = tdList[i].SelectSingleNode("./span").InnerText.Trim();
                                break;
                            case "营业期限自":
                            case "经营期限自":
                            case "合伙期限自":
                                _enterpriseInfo.term_start = tdList[i].SelectSingleNode("./span").InnerText.Trim();
                                break;
                            case "营业期限至":
                            case "经营期限至":
                            case "合伙期限至":
                                _enterpriseInfo.term_end = tdList[i].SelectSingleNode("./span").InnerText.Trim();
                                break;
                            case "经营范围":
                            case "业务范围":
                                _enterpriseInfo.scope = tdList[i].SelectSingleNode("./div/div[@class='jingyingfanwei']/span").InnerText.Replace("null", "").Replace("NULL", "");
                                break;
                            case "登记机关":
                                _enterpriseInfo.belong_org = tdList[i].SelectSingleNode("./span").InnerText.Trim();
                                break;
                            case "核准日期":
                                _enterpriseInfo.check_date = tdList[i].SelectSingleNode("./span").InnerText.Trim();
                                break;
                            case "登记状态":
                                _enterpriseInfo.status = tdList[i].SelectSingleNode("./span").InnerText.Trim();
                                break;
                            case "吊销日期":
                            case "注销日期":
                                _enterpriseInfo.end_date = tdList[i].SelectSingleNode("./span").InnerText.Trim();
                                break;
                            case "组成形式":
                                _enterpriseInfo.type_desc = tdList[i].SelectSingleNode("./span").InnerText.Trim();
                                break;
                            default:
                                break;
                        }
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(_enterpriseName))
            {
                _enterpriseName = _enterpriseInfo.name;
            }
            if (string.IsNullOrWhiteSpace(_enterpriseInfo.reg_no) && string.IsNullOrWhiteSpace(_enterpriseInfo.credit_no))
            {
                var request = this.CreateRequest();
                var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("head"));
                if (responseList != null && responseList.Any())
                {
                    this.LoadAndParseHeadInfo(responseList.First().Data);
                }
            }
        }
        #endregion

        #region 获取公司基本信息
        void LoadAndParseHeadInfo(string responseData)
        {
            if (!string.IsNullOrWhiteSpace(responseData))
            {
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                var rootNode = document.DocumentNode;
                var divs = rootNode.SelectNodes("//div[@id='basic']/div[@id='basic_center']/div");
                if (divs != null && divs.Count == 2)
                {
                    var first = divs.First();
                    _enterpriseInfo.name = first.SelectSingleNode("./span[@id='entName']").Attributes["title"].Value;
                    _enterpriseInfo.status = first.SelectSingleNode("./span[@id='zhangtai']").InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "");
                    var last = divs.Last();
                    var titles = last.SelectNodes("./div/p/span[@class='basic-info']");
                    var vals = last.SelectNodes("./div/p/span[@class='label']");
                    if (titles != null && vals != null && titles.Count == vals.Count)
                    {
                        var index = 0;
                        foreach (var title in titles)
                        {
                            switch (title.InnerText.Trim().TrimEnd(new char[] { ':', '：' }))
                            {
                                case "注册号":
                                    _enterpriseInfo.reg_no = vals[index].InnerText;
                                    break;
                                case "统一社会信用代码":
                                    _enterpriseInfo.credit_no = vals[index].InnerText;
                                    break;
                                case "首席代表":
                                case "负责人":
                                    _enterpriseInfo.oper_name = vals[index].InnerText;
                                    break;
                                case "登机机关":
                                    _enterpriseInfo.belong_org = vals[index].InnerText;
                                    break;
                                case "成立日期":
                                    _enterpriseInfo.start_date = vals[index].InnerText;
                                    break;
                                case "吊销日期":
                                case "注销日期":
                                    _enterpriseInfo.end_date = vals[index].InnerText;
                                    break;
                            }
                            index++;
                        }

                    }
                }
            }
        }
        #endregion

        #region 解析股东信息
        /// <summary>
        /// 解析股东信息
        /// </summary>
        /// <param name="rootNode"></param>
        /// <param name="_enterpriseInfo"></param>
        void LoadAndParsePartners(HtmlNode rootNode, EnterpriseInfo _enterpriseInfo)
        {
            List<Partner> partnerList = new List<Partner>();
            var request = CreateRequest();
            request.AddOrUpdateRequestParameter("pno", "1");
            List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("investor"));
            if (responseList.Count > 0)
            {
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(responseList[0].Data);
                HtmlNode rNode = doc.DocumentNode;
                var pageNode = rNode.SelectSingleNode("//div[@class='ax_image fenye']");
                var tables = rNode.SelectNodes("//table");
                if (tables.Count > 1)
                {
                    var table = tables[1];
                    LoadAndParsePartnerPage(1, table.OuterHtml, partnerList);
                    int page = int.Parse(pageNode.FirstChild.ChildNodes[1].InnerText.Replace("共", "").Replace("页", "").Replace("&nbsp;", ""));
                    for (int index = 2; index <= page; index++)
                    {
                        request.AddOrUpdateRequestParameter("pno", index.ToString());
                        responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("investor"));
                        doc = new HtmlDocument();
                        doc.LoadHtml(responseList[0].Data);
                        rNode = doc.DocumentNode; tables = rNode.SelectNodes("//table");
                        if (tables.Count > 1)
                        {
                            table = tables[1];
                            LoadAndParsePartnerPage(index, table.OuterHtml, partnerList);
                        }
                    }
                }
            }

            _enterpriseInfo.partners = partnerList;
        }

        #endregion

        #region 解析变更信息
        /// <summary>
        /// 解析变更信息
        /// </summary>
        /// <param name="rootNode"></param>
        /// <param name="_enterpriseInfo"></param>
        void LoadAndParseChangeRecords(HtmlNode rootNode, EnterpriseInfo _enterpriseInfo)
        {
            List<ChangeRecord> changeRecordList = new List<ChangeRecord>();
            var request = CreateRequest();
            request.AddOrUpdateRequestParameter("pno", "1");
            List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("alterPage"));
            if (responseList.Count > 0)
            {
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(responseList[0].Data);
                HtmlNode rNode = doc.DocumentNode;
                var pageNode = rNode.SelectSingleNode("//div[@class='ax_image fenye']");
                var tables = rNode.SelectNodes("//table");
                if (tables != null && tables.Count > 1)
                {
                    var table = tables[1];
                    LoadAndParseChangeRecords
                        (table, changeRecordList);
                    int page = int.Parse(pageNode.FirstChild.ChildNodes[1].InnerText.Replace("共", "").Replace("页", "").Replace("&nbsp;", ""));
                    for (int index = 2; index <= page; index++)
                    {
                        request.AddOrUpdateRequestParameter("pno", index.ToString());
                        responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("alterPage"));
                        doc = new HtmlDocument();
                        doc.LoadHtml(responseList[0].Data);
                        rNode = doc.DocumentNode;
                        tables = rNode.SelectNodes("//table");
                        if (tables.Count > 1)
                        {
                            table = tables[1];
                            LoadAndParseChangeRecords(table, changeRecordList);
                        }
                    }
                }
            }

            _enterpriseInfo.changerecords = changeRecordList;
        }
        private void LoadAndParseChangeRecords(HtmlNode table, List<ChangeRecord> changeRecordList)
        {
            HtmlNodeCollection rows = table.SelectNodes("./tr");
            if (rows != null)
            {
                foreach (HtmlNode rowNode in rows)
                {
                    HtmlNodeCollection cells = rowNode.SelectNodes("./td");
                    if (cells != null && cells.Count > 3)
                    {
                        ChangeRecord record = new ChangeRecord();
                        record.seq_no = changeRecordList.Count + 1;
                        record.change_item = cells[1].InnerText;
                        record.change_date = cells.Last().InnerText;
                        record.before_content = cells[2].InnerText;
                        record.after_content = cells[3].InnerText;
                        changeRecordList.Add(record);
                    }
                }
            }
        }

        #endregion

        #region 解析行政许可
        /// <summary>
        /// 行政许可
        /// </summary>
        /// <param name="_enterpriseInfo"></param>
        /// <param name="rootNode"></param>
        private void LoadAndParseLicense(EnterpriseInfo _enterpriseInfo, HtmlNode rootNode)
        {
            List<LicenseInfo> list = new List<LicenseInfo>();
            var request = CreateRequest();
            request.AddOrUpdateRequestParameter("pno", "1");
            List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("xinzhengxuke"));
            if (responseList.Count > 0)
            {
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(responseList[0].Data);
                HtmlNode rNode = doc.DocumentNode;
                var pageNode = rNode.SelectSingleNode("//div[@class='ax_image fenye']");
                var tables = rNode.SelectNodes("//table");
                if (tables != null && tables.Count > 1)
                {
                    var table = tables[1];
                    LoadAndParseLicenses
                        (table, list);
                    int page = int.Parse(pageNode.FirstChild.ChildNodes[1].InnerText.Replace("共", "").Replace("页", "").Replace("&nbsp;", ""));
                    for (int index = 2; index <= page; index++)
                    {
                        request.AddOrUpdateRequestParameter("pno", index.ToString());
                        responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("xinzhengxuke"));
                        doc = new HtmlDocument();
                        doc.LoadHtml(responseList[0].Data);
                        rNode = doc.DocumentNode;
                        tables = rNode.SelectNodes("//table");
                        if (tables.Count > 1)
                        {
                            table = tables[1];
                            LoadAndParseLicenses(table, list);
                        }
                    }
                }
            }
            _enterpriseInfo.licenses = list;
            this.LoadAndParseLicenses_Enterprise();
        }

        #endregion

        #region 解析行政许可
        /// <summary>
        /// 解析行政许可
        /// </summary>
        /// <param name="table"></param>
        /// <param name="list"></param>
        void LoadAndParseLicenses(HtmlNode table, List<LicenseInfo> list)
        {
            if (table != null)
            {
                HtmlNodeCollection rows = table.SelectNodes("./tr");
                if (rows != null)
                {
                    foreach (HtmlNode rowNode in rows)
                    {
                        HtmlNodeCollection cells = rowNode.SelectNodes("./td");
                        if (cells != null && cells.Count > 6)
                        {
                            LicenseInfo record = new LicenseInfo();
                            int index = 0;
                            int.TryParse(cells[0].InnerText, out index);
                            record.seq_no = index;
                            record.number = cells[1].InnerText;
                            record.name = cells[2].InnerText;
                            record.start_date = cells[3].InnerText;
                            record.end_date = cells[4].InnerText;
                            record.department = cells[5].InnerText;
                            record.content = cells[6].InnerText;
                            record.status = string.Empty;
                            list.Add(record);
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
        /// <param name="_enterpriseInfo"></param>
        void LoadAndParseEmployees(HtmlNode rootNode, EnterpriseInfo _enterpriseInfo)
        {
            //主要人员信息
            List<Employee> employeeList = new List<Employee>();
            var nodes = rootNode.SelectNodes("//div[@class='text keyPerInfoText']");
            if (nodes != null)
            {
                foreach (var empNode in nodes)
                {
                    Employee emp = new Employee();
                    if (empNode.HasChildNodes && empNode.SelectNodes("./center") != null)
                    {
                        emp.seq_no = employeeList.Count + 1;
                        emp.name = empNode.SelectNodes("./center")[0].SelectNodes("./p").Count > 1 ? empNode.SelectNodes("./center")[0].SelectNodes("./p")[0].InnerText : string.Empty;
                        emp.job_title = empNode.SelectNodes("./center")[0].SelectNodes("./p").Count > 1 ? empNode.SelectNodes("./center")[0].SelectNodes("./p")[1].InnerText : string.Empty;
                        if (!string.IsNullOrEmpty(emp.name) || !string.IsNullOrEmpty(emp.job_title))
                        {
                            employeeList.Add(emp);
                        }
                    }
                }
            }
            _enterpriseInfo.employees = employeeList;
        }
        #endregion

        #region 解析分支机构
        /// <summary>
        /// 解析分支机构
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="rootNode"></param>
        /// <param name="_enterpriseInfo"></param>
        void LoadAndParseBranches(string responseData, HtmlNode rootNode, EnterpriseInfo _enterpriseInfo)
        {
            List<Branch> branchList = new List<Branch>();
            var nodes = rootNode.SelectNodes("//div[@class='text fenzhiText']");
            if (nodes != null)
            {
                foreach (var branNode in nodes)
                {
                    Branch branch = new Branch();
                    if (branNode.HasChildNodes && branNode.SelectNodes("./p") != null)
                    {
                        if (branNode.SelectNodes("./p").Count > 2)
                        {
                            branch.seq_no = branchList.Count + 1;
                            branch.name = branNode.SelectNodes("./p")[0].InnerText.Trim();
                            branch.reg_no = branNode.SelectNodes("./p")[1].InnerText.Contains("：") ?
                                branNode.SelectNodes("./p")[1].InnerText.Split('：')[1].Trim() : string.Empty;
                            branch.belong_org = branNode.SelectNodes("./p")[2].InnerText.Contains("：") ?
    branNode.SelectNodes("./p")[2].InnerText.Split('：')[1].Trim() : string.Empty;
                            if (!string.IsNullOrWhiteSpace(branch.name) && branch.name != "无")
                            {
                                branchList.Add(branch);
                            }

                        }
                    }
                }
            }
            _enterpriseInfo.branches = branchList;
        }
        #endregion

        #region 解析经营异常
        /// <summary>
        /// 解析经营异常
        /// </summary>
        /// <param name="rootNode"></param>
        /// <param name="_enterpriseInfo"></param>
        void LoadAndParseAbnormal(HtmlNode rootNode, EnterpriseInfo _enterpriseInfo)
        {
            List<Branch> branchList = new List<Branch>();
            var request = CreateRequest();
            request.AddOrUpdateRequestParameter("pno", "1");
            List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("jinyinyichang"));
            if (responseList.Count > 0)
            {
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(responseList[0].Data);
                HtmlNode node = doc.DocumentNode;
                // 经营异常信息
                var div = node.SelectSingleNode("//div[@id='excDiv']");
                if (div != null)
                {
                    var table = div.SelectSingleNode("./table");
                    if (table != null)
                    {
                        HtmlNodeCollection yichangTrList = table.SelectNodes("./tr");
                        if (yichangTrList != null)
                        {
                            foreach (HtmlNode rowNode in yichangTrList)
                            {
                                var tdList = rowNode.SelectNodes("./td");
                                if (tdList != null && tdList.Count > 3)
                                {
                                    AbnormalInfo item = new AbnormalInfo();
                                    item.name = _enterpriseInfo.name;
                                    item.province = _enterpriseInfo.province;
                                    item.reg_no = string.IsNullOrWhiteSpace(_enterpriseInfo.reg_no) ? _enterpriseInfo.credit_no : _enterpriseInfo.reg_no;
                                    item.in_reason = tdList[1].InnerText;
                                    item.in_date = tdList[2].InnerText.Replace(" ", "").Replace(Environment.NewLine, "");
                                    item.out_reason = tdList[4].InnerText;
                                    item.out_date = tdList[5].InnerText.Replace(" ", "").Replace(Environment.NewLine, "");
                                    item.department = tdList[3].InnerText;

                                    _abnormals.Add(item);
                                }
                            }
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
        /// <param name="_enterpriseInfo"></param>
        void LoadAndParseSpotCheck(HtmlNode rootNode, EnterpriseInfo _enterpriseInfo)
        {
            List<Branch> branchList = new List<Branch>();
            var request = CreateRequest();
            request.AddOrUpdateRequestParameter("pno", "1");
            List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("chouchajiancha"));
            if (responseList.Count > 0)
            {
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(responseList[0].Data);
                HtmlNode node = doc.DocumentNode;
                // 抽查检查信息
                var div = node.SelectSingleNode("//div[@class='ax_table liebiaoxinxin']");
                if (div != null)
                {
                    var tables = div.SelectNodes("//table");
                    if (tables != null && tables.Count > 1)
                    {
                        HtmlNodeCollection jianchaTrList = tables[1].SelectNodes("./tr");
                        if (jianchaTrList != null)
                        {
                            foreach (HtmlNode rowNode in jianchaTrList)
                            {
                                var tdList = rowNode.SelectNodes("./td");

                                if (tdList != null && tdList.Count > 3)
                                {
                                    CheckupInfo item = new CheckupInfo();
                                    item.name = _enterpriseInfo.name;
                                    item.province = _enterpriseInfo.province;
                                    item.reg_no = string.IsNullOrWhiteSpace(_enterpriseInfo.reg_no) ? _enterpriseInfo.credit_no : _enterpriseInfo.reg_no;
                                    item.department = tdList[1].InnerText;
                                    item.type = tdList[2].InnerText.Replace(" ", "").Replace(Environment.NewLine, "");
                                    item.date = tdList[3].InnerText;
                                    item.result = tdList[4].InnerText;

                                    _checkups.Add(item);
                                }
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析工商公示信息：基本信息、股东信息、变更信息、主要人员信息、分支机构信息、经营异常信息、抽查检查信息
        /// <summary>
        /// 解析工商公示信息：基本信息、股东信息、变更信息、主要人员信息、分支机构信息、经营异常信息、抽查检查信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseTab01(string responseData)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            Parallel.Invoke(new ParallelOptions { MaxDegreeOfParallelism = 1 },
                () => this.LoadAndParsePartners(rootNode, _enterpriseInfo),
                () => this.LoadAndParseChangeRecords(rootNode, _enterpriseInfo),
                () => this.LoadAndParseEmployees(rootNode, _enterpriseInfo),
                () => this.LoadAndParseBranches(responseData, rootNode, _enterpriseInfo),
                () => this.LoadAndParseAbnormal(rootNode, _enterpriseInfo),
                () => this.LoadAndParseSpotCheck(rootNode, _enterpriseInfo),
                () => this.LoadAndParseMortgage(_enterpriseInfo, rootNode),
                () => this.LoadAndParsePledge(_enterpriseInfo, rootNode),
                () => this.LoadAndParseLicense(_enterpriseInfo, rootNode),
                () => this.LoadAndParseKnowledgeProperty(_enterpriseInfo, rootNode),
                () => this.LoadAndParseStockChanges(_enterpriseInfo, rootNode),
                () => this.LoadAndParseFinancialContribution(_enterpriseInfo, rootNode),
                () => this.LoadAndParseReportsOnly(_enterpriseInfo, rootNode),
                () => this.LoadAndParsePunishment(),
                () => this.LoadAndParseJudicial(responseData,_enterpriseInfo)
                );
        }

        #endregion

        #region 解析股东及出资信息
        /// <summary>
        /// 股东及出资信息
        /// </summary>
        /// <param name="_enterpriseInfo"></param>
        /// <param name="rootNode"></param>
        private void LoadAndParseFinancialContribution(EnterpriseInfo _enterpriseInfo, HtmlNode rootNode)
        {
            List<FinancialContribution> list = new List<FinancialContribution>();
            var request = CreateRequest();
            request.AddOrUpdateRequestParameter("pno", "1");
            request.AddOrUpdateRequestParameter("bodyid", bodyid);
            List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("financialcontribution"));
            if (responseList.Count > 0)
            {
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(responseList[0].Data);
                HtmlNode rNode = doc.DocumentNode;
                var pageNode = rNode.SelectSingleNode("//div[@class='ax_image fenye']");
                var tables = rNode.SelectNodes("//table");
                if (tables != null && tables.Count > 1)
                {
                    var table = tables[1];
                    LoadFC(table.OuterHtml, list);
                    int page = int.Parse(pageNode.FirstChild.ChildNodes[1].InnerText.Replace("共", "").Replace("页", "").Replace("&nbsp;", ""));
                    for (int index = 2; index <= page; index++)
                    {
                        request.AddOrUpdateRequestParameter("pno", index.ToString());
                        responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("financialcontribution"));
                        doc = new HtmlDocument();
                        doc.LoadHtml(responseList[0].Data);
                        rNode = doc.DocumentNode; tables = rNode.SelectNodes("//table");
                        if (tables.Count > 1)
                        {
                            table = tables[1];
                            LoadFC(table.OuterHtml, list);
                        }
                    }
                }
            }
            _enterpriseInfo.financial_contributions = list;
        }


        private void LoadFC(string response, List<FinancialContribution> list)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(response);
            HtmlNode node = document.DocumentNode;
            HtmlNode table = node.SelectSingleNode("//table");
            if (table != null)
            {
                HtmlNodeCollection rows = table.SelectNodes("./tr");
                if (rows == null) return;
                foreach (HtmlNode rowNode in rows)
                {
                    HtmlNodeCollection cells = rowNode.SelectNodes("./td");
                    if (cells != null)
                    {
                        if (cells.Count > 8)
                        {
                            FinancialContribution item = new FinancialContribution();
                            item.seq_no = list.Count + 1;
                            item.investor_name = cells[1].InnerText;
                            item.total_should_capi = cells[2].InnerText;
                            item.total_real_capi = cells[3].InnerText;
                            FinancialContribution.ShouldCapiItem sc = new FinancialContribution.ShouldCapiItem();
                            sc.should_invest_type = cells[4].InnerText;
                            sc.should_capi = cells[5].InnerText;
                            sc.should_invest_date = cells[6].InnerText;
                            sc.public_date = cells[7].InnerText;
                            if (!string.IsNullOrWhiteSpace(sc.should_invest_type) || !string.IsNullOrWhiteSpace(sc.should_capi) || !string.IsNullOrWhiteSpace(sc.should_invest_date))
                            {
                                item.should_capi_items.Add(sc);
                            }
                            FinancialContribution.RealCapiItem rc = new FinancialContribution.RealCapiItem();
                            rc.real_invest_type = cells[8].InnerText;
                            rc.real_capi = cells[9].InnerText;
                            rc.real_invest_date = cells[10].InnerText;
                            rc.public_date = cells[11].InnerText;
                            if (!string.IsNullOrWhiteSpace(rc.real_invest_type) || !string.IsNullOrWhiteSpace(rc.real_capi) || !string.IsNullOrWhiteSpace(rc.real_invest_date))
                            {
                                item.real_capi_items.Add(rc);
                            }

                            list.Add(item);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析股权出质
        /// <summary>
        ///解析股权出质
        /// </summary>
        /// <param name="responseInfoList"></param>
        /// <param name="mortgageInfo"></param>
        private void LoadAndParsePledge(EnterpriseInfo _enterpriseInfo, HtmlNode rootNode)
        {
            List<EquityQuality> list = new List<EquityQuality>();
            var request = CreateRequest();
            request.AddOrUpdateRequestParameter("pno", "1");
            List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("guquanchizhi"));
            if (responseList.Count > 0)
            {
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(responseList[0].Data);
                HtmlNode rNode = doc.DocumentNode;
                var pageNode = rNode.SelectSingleNode("//div[@class='ax_image fenye']");
                var tables = rNode.SelectNodes("//table");
                if (tables != null && tables.Count > 1)
                {
                    var table = tables[1];
                    LoadGuQuanChuZi(table.OuterHtml, list);
                    int page = int.Parse(pageNode.FirstChild.ChildNodes[1].InnerText.Replace("共", "").Replace("页", "").Replace("&nbsp;", ""));
                    for (int index = 2; index <= page; index++)
                    {
                        request.AddOrUpdateRequestParameter("pno", index.ToString());
                        responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("guquanchizhi"));
                        doc = new HtmlDocument();
                        doc.LoadHtml(responseList[0].Data);
                        rNode = doc.DocumentNode; tables = rNode.SelectNodes("//table");
                        if (tables.Count > 1)
                        {
                            table = tables[1];
                            LoadGuQuanChuZi(table.OuterHtml, list);
                        }
                    }
                }
            }
            _enterpriseInfo.equity_qualities = list;
        }

        private void LoadGuQuanChuZi(string html, List<EquityQuality> list)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(html);
            HtmlNode node = document.DocumentNode;
            HtmlNode table = node.SelectSingleNode("//table");
            if (table != null)
            {
                HtmlNodeCollection rows = table.SelectNodes("./tr");
                if (rows == null) return;
                foreach (HtmlNode rowNode in rows)
                {
                    HtmlNodeCollection cells = rowNode.SelectNodes("./td");
                    if (cells != null && cells.Count > 9)
                    {
                        EquityQuality item = new EquityQuality();
                        item.seq_no = list.Count + 1;
                        item.number = cells[1].InnerText;
                        item.pledgor = cells[2].InnerText;
                        item.pledgor_identify_no = cells[3].InnerText;
                        item.pledgor_amount = cells[4].InnerText;
                        item.pawnee = cells[5].InnerText;
                        item.pawnee_identify_no = cells[6].InnerText;
                        item.date = cells[7].InnerText;
                        item.status = cells[8].InnerText;
                        item.public_date = cells[9].InnerText;
                        list.Add(item);
                    }
                }
            }

        }

        #endregion

        #region 解析股权变更
        /// <summary>
        /// 股权变更
        /// </summary>
        /// <param name="_enterpriseInfo"></param>
        /// <param name="rootNode"></param>
        private void LoadAndParseStockChanges(EnterpriseInfo _enterpriseInfo, HtmlNode rootNode)
        {
            List<StockChangeItem> list = new List<StockChangeItem>();
            var request = CreateRequest();
            request.AddOrUpdateRequestParameter("pno", "1");
            request.AddOrUpdateRequestParameter("bodyid", bodyid);
            List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("guquanbiangeng"));
            if (responseList.Count > 0)
            {
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(responseList[0].Data);
                HtmlNode rNode = doc.DocumentNode;
                var pageNode = rNode.SelectSingleNode("//div[@class='ax_image fenye']");
                var tables = rNode.SelectNodes("//table");
                if (tables != null && tables.Count > 1)
                {
                    var table = tables[1];
                    LoadStockChanges(table.OuterHtml, list);
                    int page = int.Parse(pageNode.FirstChild.ChildNodes[1].InnerText.Replace("共", "").Replace("页", "").Replace("&nbsp;", ""));
                    for (int index = 2; index <= page; index++)
                    {
                        request.AddOrUpdateRequestParameter("pno", index.ToString());
                        responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("guquanbiangeng"));
                        doc = new HtmlDocument();
                        doc.LoadHtml(responseList[0].Data);
                        rNode = doc.DocumentNode; tables = rNode.SelectNodes("//table");
                        if (tables.Count > 1)
                        {
                            table = tables[1];
                            LoadStockChanges(table.OuterHtml, list);
                        }
                    }
                }
            }

            _enterpriseInfo.stock_changes = list;
        }

        private void LoadStockChanges(string response, List<StockChangeItem> list)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(response);
            HtmlNode node = document.DocumentNode;
            HtmlNode table = node.SelectSingleNode("//table");
            if (table != null)
            {
                HtmlNodeCollection rows = table.SelectNodes("./tr");
                if (rows == null) return;
                foreach (HtmlNode rowNode in rows)
                {
                    HtmlNodeCollection cells = rowNode.SelectNodes("./td");
                    if (cells != null && cells.Count > 4)
                    {
                        StockChangeItem record = new StockChangeItem();
                        record.seq_no = list.Count + 1;
                        record.name = cells[1].InnerText;
                        record.before_percent = cells[2].InnerText;
                        record.after_percent = cells[3].InnerText;
                        record.change_date = cells[4].InnerText;
                        record.public_date = cells[5].InnerText;
                        list.Add(record);
                    }
                }
            }
        }

        #endregion

        #region 解析行政许可--企业
        void LoadAndParseLicenses_Enterprise()
        {
            var request = CreateRequest();
            request.AddOrUpdateRequestParameter("pno", "1");
            request.AddOrUpdateRequestParameter("bodyid", bodyid);
            List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("enterprise_license"));
            if (responseList.Count > 0)
            {
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(responseList[0].Data);
                HtmlNode rNode = doc.DocumentNode;
                var pageNode = rNode.SelectSingleNode("//div[@class='ax_image fenye']");
                var tables = rNode.SelectNodes("//table");
                if (tables != null && tables.Count > 1)
                {
                    var table = tables[1];
                    LoadAndParseLN(table);
                    int page = int.Parse(pageNode.FirstChild.ChildNodes[1].InnerText.Replace("共", "").Replace("页", "").Replace("&nbsp;", ""));
                    for (int index = 2; index <= page; index++)
                    {
                        request.AddOrUpdateRequestParameter("pno", index.ToString());
                        responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("enterprise_license"));
                        doc = new HtmlDocument();
                        doc.LoadHtml(responseList[0].Data);
                        rNode = doc.DocumentNode;
                        tables = rNode.SelectNodes("//table");
                        if (tables.Count > 1)
                        {
                            table = tables[1];
                            LoadAndParseLN(table);
                        }
                    }
                }
            }
        }

        void LoadAndParseLN(HtmlNode table)
        {
            var trs = table.SelectNodes("./tr");
            if (trs != null && trs.Any())
            {
                foreach (var tr in trs)
                {
                    var tds = tr.SelectNodes("./td");
                    if (tds != null && tds.Any())
                    {
                        LicenseInfo item = new LicenseInfo();
                        item.seq_no = _enterpriseInfo.licenses.Count + 1;
                        item.number = tds[1].InnerText;
                        item.name = tds[2].InnerText;
                        item.start_date = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        item.end_date = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        item.department = tds[5].InnerText;
                        item.content = tds[6].InnerText;
                        item.status = tds[7].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                        if (_enterpriseInfo.licenses.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.number) && p.number == item.number) == null)
                        {
                            _enterpriseInfo.licenses.Add(item);
                        }
                    }

                }

            }

        }
        #endregion

        #region 解析知识产权
        /// <summary>
        /// 知识产权
        /// </summary>
        /// <param name="_enterpriseInfo"></param>
        /// <param name="rootNode"></param>
        private void LoadAndParseKnowledgeProperty(EnterpriseInfo _enterpriseInfo, HtmlNode rootNode)
        {

            List<KnowledgeProperty> list = new List<KnowledgeProperty>();
            var request = CreateRequest();
            request.AddOrUpdateRequestParameter("pno", "1");
            request.AddOrUpdateRequestParameter("bodyid", bodyid);
            List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("zhishichanquan"));
            if (responseList.Count > 0)
            {
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(responseList[0].Data);
                HtmlNode rNode = doc.DocumentNode;
                var pageNode = rNode.SelectSingleNode("//div[@class='ax_image fenye']");
                var tables = rNode.SelectNodes("//table");
                if (tables != null && tables.Count > 1)
                {
                    var table = tables[1];
                    LoadAndParseKN
                        (table.OuterHtml, list);
                    int page = int.Parse(pageNode.FirstChild.ChildNodes[1].InnerText.Replace("共", "").Replace("页", "").Replace("&nbsp;", ""));
                    for (int index = 2; index <= page; index++)
                    {
                        request.AddOrUpdateRequestParameter("pno", index.ToString());
                        responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("zhishichanquan"));
                        doc = new HtmlDocument();
                        doc.LoadHtml(responseList[0].Data);
                        rNode = doc.DocumentNode;
                        tables = rNode.SelectNodes("//table");
                        if (tables.Count > 1)
                        {
                            table = tables[1];
                            LoadAndParseKN(table.OuterHtml, list);
                        }
                    }
                }
            }
            _enterpriseInfo.knowledge_properties = list;
        }


        private void LoadAndParseKN(string reponse, List<KnowledgeProperty> list)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(reponse);
            HtmlNode node = document.DocumentNode;
            HtmlNode table = node.SelectSingleNode("//table");
            if (table != null)
            {
                HtmlNodeCollection rows = table.SelectNodes("./tr");
                if (rows == null) return;
                foreach (HtmlNode rowNode in rows)
                {
                    HtmlNodeCollection cells = rowNode.SelectNodes("./td");
                    if (cells != null && cells.Count > 8)
                    {
                        KnowledgeProperty property = new KnowledgeProperty();
                        property.seq_no = list.Count + 1;
                        property.number = cells[1].InnerText;
                        property.name = cells[2].InnerText;
                        property.type = cells[3].InnerText;
                        property.pledgor = cells[4].InnerText;
                        property.pawnee = cells[5].InnerText;
                        property.period = cells[6].InnerText;
                        property.status = cells[7].InnerText;
                        property.public_date = cells[8].InnerText;
                        list.Add(property);
                    }
                }
            }
        }
        #endregion

        #region 解析动产抵押
        /// <summary>
        /// 解析动产抵押
        /// </summary>
        /// <param name="_enterpriseInfo"></param>
        /// <param name="rootNode"></param>
        private void LoadAndParseMortgage(EnterpriseInfo _enterpriseInfo, HtmlNode rootNode)
        {

            List<MortgageInfo> mortgages = new List<MortgageInfo>();
            var request = CreateRequest();
            request.AddOrUpdateRequestParameter("pno", "1");
            List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("dongchandiya"));
            if (responseList.Count > 0)
            {
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(responseList[0].Data);
                HtmlNode rNode = doc.DocumentNode;
                var pageNode = rNode.SelectSingleNode("//div[@class='ax_image fenye']");
                var tables = rNode.SelectNodes("//table");
                if (tables != null && tables.Count > 1)
                {
                    var table = tables[1];
                    ProceedDongChanDiYa(table.OuterHtml, mortgages);
                    int page = int.Parse(pageNode.FirstChild.ChildNodes[1].InnerText.Replace("共", "").Replace("页", "").Replace("&nbsp;", ""));
                    for (int index = 2; index <= page; index++)
                    {
                        request.AddOrUpdateRequestParameter("pno", index.ToString());
                        responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("dongchandiya"));
                        doc = new HtmlDocument();
                        doc.LoadHtml(responseList[0].Data);
                        rNode = doc.DocumentNode; tables = rNode.SelectNodes("//table");
                        if (tables.Count > 1)
                        {
                            table = tables[1];
                            ProceedDongChanDiYa(table.OuterHtml, mortgages);
                        }
                    }
                }
            }
            _enterpriseInfo.mortgages = mortgages;

        }

        private void ProceedDongChanDiYa(string html, List<MortgageInfo> mortgages)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(html);
            HtmlNode node = document.DocumentNode;
            HtmlNodeCollection rows = node.SelectNodes("//table/tr");
            if (rows != null && rows.Count > 0)
            {
                foreach (HtmlNode rowNode in rows)
                {
                    HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                    if (tdList != null && tdList.Count > 7)
                    {
                        MortgageInfo item = new MortgageInfo();
                        item.seq_no = mortgages.Count + 1;
                        item.number = tdList[1].InnerText;
                        item.date = tdList[2].InnerText;
                        item.department = tdList[3].InnerText;
                        item.amount = tdList[4].InnerText;
                        item.status = tdList[5].InnerText;
                        item.public_date = tdList[6].InnerText;
                        string link = tdList[7].InnerHtml;
                        var match = Regex.Match(link, "seeMovable.*?\\)");
                        if (match != null && match.Success)
                        {
                            string id = match.Value.Split('\'')[1];
                            var responseInfo = this._request.RequestData(new RequestSetting("get", "http://sx.gsxt.gov.cn/business/mortInfoDetail.jspx?id=" + id, "", "0", "dongchandiyaxiangqing"));
                            ProceedDongChanDiYaDetail(responseInfo, item);
                        }

                        mortgages.Add(item);
                    }
                }

            }
        }


        private void ProceedDongChanDiYaDetail(ResponseInfo responseInfo, MortgageInfo mortgageInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseInfo.Data);
            HtmlNode rootNode = document.DocumentNode;
            HtmlNodeCollection tables = rootNode.SelectNodes("//table[@class='detailsList']");
            if (tables == null) return;
            for (var index = 0; index < tables.Count; index++)
            {
                var table = tables[index];
                var rows = table.SelectNodes("./tr");
                if (rows != null && rows.Count > 0)
                {
                    if (index > 1)
                    {
                        var titletable = tables[index - 1];
                        if (titletable.InnerText.Contains("抵押权人证照/证件类型"))
                        {
                            foreach (var row in rows)
                            {
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
                }
                if (rows != null && rows[0].InnerText.Contains("种类"))
                {
                    foreach (HtmlNode rowNode in rows)
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
                if (rows != null && index > 1)
                {
                    var titletable = tables[index - 1];
                    if (titletable.InnerText.Contains("所有权或使用权归属"))
                    {
                        foreach (var row in rows)
                        {
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

        #region 解析股东的分页
        /// <summary>
        /// 解析股东的分页
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParsePartnerPage(int page, string responseData, List<Partner> partnerList)
        {
            int seqno = (page - 1) * 5;
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            HtmlNodeCollection trList = rootNode.SelectNodes("//tr");

            foreach (HtmlNode rowNode in trList)
            {
                HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                if (tdList != null && tdList.Count == 5 && tdList.Last().SelectSingleNode("./a") == null)
                {
                    Partner partner = new Partner();

                    partner.ex_id = "";
                    partner.seq_no = ++seqno;
                    partner.stock_name = tdList[1].InnerText.Trim();
                    partner.stock_type = tdList[2].InnerText.Trim();
                    partner.identify_type = tdList[3].InnerText.Trim();
                    partner.identify_no = tdList[4].InnerText.Trim();

                    partner.should_capi_items = new List<ShouldCapiItem>();
                    partner.real_capi_items = new List<RealCapiItem>();
                    partnerList.Add(partner);
                }
                else if (tdList != null && tdList.Count == 6)
                {
                    Partner partner = new Partner();
                    partner.ex_id = "";
                    partner.seq_no = ++seqno;
                    partner.stock_name = tdList[1].InnerText.Trim();
                    partner.stock_percent = "";
                    partner.stock_type = tdList[2].InnerText.Trim();
                    partner.should_capi_items = new List<ShouldCapiItem>();
                    partner.real_capi_items = new List<RealCapiItem>();
                    partner.identify_no = tdList[4].InnerText.Trim();
                    partner.identify_type = tdList[3].InnerText.Trim();
                    var uuid = string.Empty;
                    //解析股东详情
                    if (tdList.Last().SelectSingleNode("./a") != null)
                    {
                        uuid = tdList.Last().SelectSingleNode("./a").Attributes["onclick"].Value.Split('\'')[1];
                        partner.ex_id = uuid;
                        //解析股东详情
                        var request = CreateRequest();
                        request.AddOrUpdateRequestParameter("invId", uuid.Trim());
                        List<ResponseInfo> reponseList = request.GetResponseInfo(_requestXml.GetRequestListByName("investor_detials"));
                        if (reponseList != null && reponseList.Count() > 0)
                        {
                            LoadAndParseInvestorDetails(partner, reponseList[0].Data);
                        }
                    }
                    partnerList.Add(partner);
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
            if (string.IsNullOrWhiteSpace(responseData)) return;
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);

            HtmlNode rootNode = document.DocumentNode;
            var tables = rootNode.SelectNodes("//table[@class='detailsList']");
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
                HtmlNodeCollection trList = shouldTable.SelectNodes("./tr");

                foreach (HtmlNode rowNode in trList)
                {
                    HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                    if (tdList != null && tdList.Count > 2)
                    {
                        ShouldCapiItem sItem = new ShouldCapiItem();
                        var sCapi = tdList[1].InnerText.Trim();
                        sItem.shoud_capi = string.IsNullOrEmpty(sCapi) ? "" : sCapi;
                        sItem.should_capi_date = tdList[2].InnerText.Trim();
                        sItem.invest_type = tdList[0].InnerText.Trim();
                        if (string.IsNullOrWhiteSpace(sItem.shoud_capi)
                               && string.IsNullOrWhiteSpace(sItem.should_capi_date)
                               && string.IsNullOrWhiteSpace(sItem.invest_type))
                        {
                            continue;
                        }
                        partner.should_capi_items.Add(sItem);
                    }
                }
                if (tables.Count > 2)
                {
                    HtmlNode realTable = tables[2];
                    trList = realTable.SelectNodes("./tr");
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                        if (tdList != null && tdList.Count > 2)
                        {
                            RealCapiItem rItem = new RealCapiItem();
                            rItem.real_capi = tdList[1].InnerText.Trim();
                            rItem.real_capi_date = tdList[2].InnerText.Trim();
                            rItem.invest_type = tdList[0].InnerText.Trim();
                            if (string.IsNullOrWhiteSpace(rItem.real_capi)
                                && string.IsNullOrWhiteSpace(rItem.real_capi_date)
                                && string.IsNullOrWhiteSpace(rItem.invest_type))
                            {
                                continue;
                            }
                            partner.real_capi_items.Add(rItem);
                        }
                    }

                }
            }
            if (partner.should_capi_items.Count == 0)
            {
                var trs = tables[0].SelectNodes("./tr");
                if (trs != null)
                {
                    foreach (var tr in trs)
                    {
                        var th = tr.SelectSingleNode("./th");
                        var td = tr.SelectSingleNode("./td");
                        if (th.InnerText.Contains("认缴额"))
                        {
                            ShouldCapiItem sItem = new ShouldCapiItem();
                            sItem.shoud_capi = td.InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            partner.should_capi_items.Add(sItem);
                            break;
                        }
                    }
                }
            }
            if (partner.real_capi_items.Count == 0)
            {
                var trs = tables[0].SelectNodes("./tr");
                if (trs != null)
                {
                    foreach (var tr in trs)
                    {
                        var th = tr.SelectSingleNode("./th");
                        var td = tr.SelectSingleNode("./td");
                        if (th.InnerText.Contains("实缴额"))
                        {
                            RealCapiItem rItem = new RealCapiItem();
                            rItem.real_capi = td.InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            partner.real_capi_items.Add(rItem);
                            break;
                        }
                    }
                }
            }
        }

        #endregion

        #region 解析变更的分页
        /// <summary>
        /// 解析变更的分页
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseAlterPage(string responseData, List<ChangeRecord> changeRecordList)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            HtmlNodeCollection trList = rootNode.SelectNodes("//tr");
            if (trList != null)
            {
                foreach (HtmlNode rowNode in trList)
                {
                    HtmlNodeCollection tddList = rowNode.SelectNodes("./td");
                    ChangeRecord changeRecord = new ChangeRecord();
                    if (tddList != null && tddList.Count > 3)
                    {
                        changeRecord.change_item = tddList[0].InnerText.Trim();
                        changeRecord.before_content = tddList[1].InnerText.Trim();
                        changeRecord.after_content = tddList[2].InnerText.Trim();
                        changeRecord.change_date = tddList[3].InnerText.Trim();
                        changeRecord.seq_no = changeRecordList.Count + 1;
                        changeRecordList.Add(changeRecord);
                    }
                }
            }

        }

        #endregion

        #region 解析分支机构的分页
        /// <summary>
        /// 解析分支机构的分页
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseBranchPage(string responseData, List<Branch> branchList)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            HtmlNodeCollection trList = rootNode.SelectNodes("//tr");

            foreach (HtmlNode rowNode in trList)
            {
                HtmlNodeCollection tdList = rowNode.SelectNodes("./td");

                if (tdList != null && tdList.Count > 3)
                {
                    Branch branch = new Branch();
                    branch.seq_no = branchList.Count + 1;
                    branch.belong_org = tdList[3].InnerText.Trim();
                    branch.name = tdList[2].InnerText.Trim();
                    branch.oper_name = "";
                    branch.reg_no = tdList[1].InnerText.Trim();

                    branchList.Add(branch);
                }
            }

        }

        #endregion

        #region 解析年报信息
        /// <summary>
        /// 解析年报信息
        /// </summary>
        /// <param name="_enterpriseInfo"></param>
        /// <param name="rootNode"></param>
        void LoadAndParseReportsOnly(EnterpriseInfo _enterpriseInfo, HtmlNode rootNode)
        {
            try
            {
                var request = CreateRequest();
                List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("report"));
                List<Report> reportList = new List<Report>();
                if (responseList.Count > 0 && !string.IsNullOrEmpty(responseList[0].Data))
                {
                    HtmlDocument doc = new HtmlDocument();
                    doc.LoadHtml(responseList[0].Data);
                    rootNode = doc.DocumentNode;
                    HtmlNode table = rootNode.SelectSingleNode("//div[@id='yearreportTable']/table");
                    if (table != null)
                    {
                        HtmlNodeCollection trList = table.SelectNodes("./tr");
                        Parallel.ForEach(trList, new ParallelOptions { MaxDegreeOfParallelism = 5 }, rowNode => this.LoadAndParseReports_Parallel(rowNode, reportList));
                    }
                    if (reportList.Any())
                    {
                        reportList.Sort(new ReportComparer());
                    }
                    _enterpriseInfo.reports = reportList;
                }
            }
            catch (Exception ex)
            {
                _enterpriseInfo.reports.Clear();
                Console.WriteLine("Exception when LoadAndParseReport.." + ex.ToString());
                LogHelper.Error("Exception when LoadAndParseReport.." + ex.ToString());
            }
        }

        #endregion

        #region 解析年报--并行
        /// <summary>
        /// 解析年报--并行
        /// </summary>
        /// <param name="rowNode"></param>
        /// <param name="reportList"></param>
        void LoadAndParseReports_Parallel(HtmlNode rowNode, List<Report> reportList)
        {
            var tds = rowNode.SelectNodes("./td");
            if (tds != null && tds.Any())
            {
                Report report = new Report();
                var reportDetailId = string.Empty;
                report.report_year = tds[1].InnerText.Substring(0, 4);
                report.report_name = tds[1].InnerText;
                report.report_date = tds[2].InnerText;
                var a = tds.Last().SelectSingleNode("./a");
                if (a == null)
                {
                    reportList.Add(report);
                    return;
                }
                var reportHerf = a.Attributes.Contains("href") ? a.Attributes["href"].Value : string.Empty;
                if (!string.IsNullOrEmpty(report.report_year))
                {
                    if (!reportsNeedToLoad.Any() || reportsNeedToLoad.Contains(report.report_year))
                    {
                        if (!string.IsNullOrEmpty(reportHerf))
                        {
                            // 加载解析年报详细信息
                            var request = CreateRequest();
                            //request.AddOrUpdateRequestParameter("reportDetailId", reportDetailId);
                            request.AddOrUpdateRequestParameter("reportdetail_url", reportHerf);
                            List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("reportdetail"));
                            if (responseList != null && responseList.Count > 0)
                            {
                                LoadAndParseReportDetail(responseList[0].Data, report);
                            }
                        }
                        reportList.Add(report);
                    }
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
        private void LoadAndParseReportDetail(string responseData, Report report)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var date = Regex.Match(responseData, "填报时间：.*?</span>");
            if (date != null && date.Success && !string.IsNullOrEmpty(date.Value))
            {
                report.report_date = date.Value.Replace("填报时间：", "").Replace("</span>", "").Trim();
            }

            HtmlNodeCollection tables = rootNode.SelectNodes("//table");
            var contents = rootNode.SelectNodes("//div[@class='webContent']");
            if (contents != null)
            {
                foreach (var contente in contents)
                {
                    var ps = contente.SelectNodes("./p");
                    if (ps != null && ps.Count == 3)
                    {
                        WebsiteItem item = new WebsiteItem();
                        item.seq_no = report.websites.Count + 1;
                        item.web_type = ps[0].InnerText;
                        item.web_name = ps[1].InnerText.Replace("· 类型：", "").Replace("&nbsp;", "").Trim();
                        item.web_url = ps[2].InnerText.Replace("· 网址：", "").Replace("&nbsp;", "").Trim();
                        report.websites.Add(item);
                    }
                    else if (ps != null && ps.Count == 2)
                    {
                        InvestItem item = new InvestItem();
                        item.seq_no = report.invest_items.Count + 1;
                        item.invest_name = ps[0].InnerText;
                        item.invest_reg_no = ps[1].InnerText.Replace("·", "").Replace("统一社会信用代码/注册号：", "").Replace("&nbsp;", "").Trim();
                        report.invest_items.Add(item);
                    }
                }
            }
            if (tables != null)
            {
                foreach (HtmlNode table in tables)
                {
                    if (table.SelectNodes("./tr/td") == null) continue;
                    string title = table.SelectNodes("./tr/td")[0].InnerText.Trim();
                    if (title.Trim() == ("基本信息"))
                    {
                        // 企业基本信息
                        HtmlNodeCollection trList = table.SelectNodes("./tr");
                        foreach (HtmlNode rowNode in trList)
                        {
                            HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                            if (tdList != null && tdList != null)
                            {
                                for (int i = 0; i < tdList.Count; i++)
                                {
                                    if (tdList == null) break;
                                    var spans = tdList[i].SelectNodes("./span");
                                    if (spans != null && spans.Any())
                                    {
                                        var spanTitle = spans.First().InnerText.Replace("\r;", "").Replace("\n", "").Replace("\t", "").Replace("&nbsp;", "").Replace("：", "").Replace(":", "").Replace("·", "").Trim();
                                        var spanVaule = spans.Last().InnerText.Replace("\r;", "").Replace("\n", "").Replace("\t", "").Replace("&nbsp;", "").Trim();
                                        switch (spanTitle)
                                        {
                                            case "注册号":
                                            case "营业执照注册号":
                                                report.reg_no = spanVaule;
                                                break;
                                            case "统一社会信用代码":
                                                report.credit_no = spanVaule;
                                                break;
                                            case "注册号/统一社会信用代码":
                                            case "统一社会信用代码/注册号":
                                                if (spanVaule.Contains("/") && spanVaule.Length > 18)
                                                {
                                                    var arr = spanVaule.Split('/');
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
                                                    if (spanVaule.Length == 18)
                                                    {
                                                        report.credit_no = spanVaule;
                                                    }
                                                    else
                                                    {
                                                        report.reg_no = spanVaule;
                                                    }
                                                }

                                                break;
                                            case "企业名称":
                                            case "合作社名称":
                                            case "名称":
                                            case "个体户名称":
                                            case "农专社名称":
                                                report.name = spanVaule.Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                                                break;
                                            case "企业联系电话":
                                            case "联系电话":
                                            case "经营者联系电话":
                                                report.telephone = spanVaule;
                                                break;
                                            case "企业通信地址":
                                                report.address = spanVaule;
                                                break;
                                            case "邮政编码":
                                                report.zip_code = spanVaule;
                                                break;
                                            case "电子邮箱":
                                            case "企业电子邮箱":
                                                report.email = spanVaule;
                                                break;
                                            case "是否有投资信息或购买其他公司股权":
                                            case "是否有对外投资设立企业信息":
                                                report.if_invest = spanVaule;
                                                break;
                                            case "对外提供保证担保信息":
                                            case "是否对外提供保证担保信息":
                                                report.if_external_guarantee = spanVaule;
                                                break;
                                            case "是否有网站或网店":
                                            case "是否有网站或网点":
                                                report.if_website = spanVaule;
                                                break;
                                            case "企业经营状态":
                                                report.status = spanVaule;
                                                break;
                                            case "从业人数":
                                            case "成员人数":
                                                report.collegues_num = spanVaule;
                                                break;
                                            case "有限责任公司本年度是否发生股东股权转让":
                                                report.if_equity = spanVaule;
                                                break;
                                            case "经营者姓名":
                                                report.oper_name = spanVaule;
                                                break;
                                            case "资金数额":
                                                report.reg_capi = spanVaule;
                                                break;
                                            default:
                                                break;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    else if (title.Trim() == "企业资产状况信息" || title.Trim() == "生产经营情况信息" || title.Trim() == "资产状况信息")
                    {
                        HtmlNodeCollection trList = table.SelectNodes("./tr");

                        foreach (HtmlNode rowNode in trList)
                        {
                            HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                            if (tdList == null) break;
                            for (int i = 0; i < tdList.Count; i++)
                            {
                                switch (tdList[i].InnerText.Replace("&nbsp;", "").Trim())
                                {
                                    case "资产总额":
                                        report.total_equity = tdList[i + 1].InnerText.Replace("&nbsp;", "").Trim();
                                        break;
                                    case "负债总额":
                                    case "金融贷款":
                                        report.debit_amount = tdList[i + 1].InnerText.Replace("&nbsp;", "").Trim();
                                        break;
                                    case "销售总额":
                                    case "营业总收入":
                                    case "营业额或营业收入":
                                    case "销售额或营业收入":
                                        report.sale_income = tdList[i + 1].InnerText.Replace("&nbsp;", "").Trim();
                                        break;
                                    case "其中：主营业务收入":
                                    case "营业总收入中主营业务收入":
                                        report.serv_fare_income = tdList[i + 1].InnerText.Replace("&nbsp;", "").Trim();
                                        break;
                                    case "利润总额":
                                    case "盈余总额":
                                        report.profit_total = tdList[i + 1].InnerText.Replace("&nbsp;", "").Trim();
                                        break;
                                    case "净利润":
                                        report.net_amount = tdList[i + 1].InnerText.Replace("&nbsp;", "").Trim();
                                        break;
                                    case "纳税总额":
                                    case "纳税金额":
                                        report.tax_total = tdList[i + 1].InnerText.Replace("&nbsp;", "").Trim();
                                        break;
                                    case "所得者权益合计":
                                    case "所有者权益合计":
                                    case "获得政府扶持资金、补助":
                                        report.profit_reta = tdList[i + 1].InnerText.Replace("&nbsp;", "").Trim();
                                        break;
                                    default:
                                        break;
                                }

                            }

                        }

                    }
                    else if (title.Contains("城镇职工基本养老保险"))
                    {
                        HtmlNodeCollection trList = table.ParentNode.SelectNodes("./table/tr");

                        foreach (HtmlNode rowNode in trList)
                        {
                            HtmlNodeCollection thList = rowNode.SelectNodes("./td/span");
                            HtmlNodeCollection tdList = rowNode.SelectNodes("./td");

                            if (thList != null && tdList != null)
                            {
                                foreach (var thItem in thList)
                                {
                                    tdList.Remove(thItem.ParentNode);
                                }
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
                }
            }
            this.LoadAndParseReportPartner(responseData, report);
            this.LoadAndParseReportExternalGuarantee(responseData, report);
            this.LoadAndParseReportStockChange(responseData, report);
            this.LoadAndParseReportUpdateRecord(responseData, report);
        }
        #endregion

        #region 解析年报修改信息
        /// <summary>
        /// 解析年报修改信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        void LoadAndParseReportUpdateRecord(string responseData, Report report)
        {
            var str = "var modifyListJsonStr='";
            if (responseData.Contains(str))
            {
                var tempStr = responseData.Substring(responseData.IndexOf(str) + str.Length);
                tempStr = tempStr.Substring(0, tempStr.IndexOf("';"));
                object[] objs = { new { descr = "", modifyBefore = "", modifyAfter = "", modifyDate = 0 } };
                var list = JsonConvert.DeserializeAnonymousType(tempStr, objs);
                if (list != null && list.Any())
                {
                    foreach (object obj in list)
                    {
                        BsonDocument document = BsonDocument.Parse(obj.ToString());
                        UpdateRecord item = new UpdateRecord();
                        item.seq_no = report.update_records.Count + 1;
                        item.update_item = document.Contains("descr") ? (document["descr"].IsBsonNull ? string.Empty : document["descr"].AsString) : string.Empty;
                        if (document.Contains("modifyBefore"))
                        {
                            if (document["modifyBefore"].IsBsonNull)
                            {
                                item.before_update = string.Empty;
                            }
                            else if (document["modifyBefore"].BsonType == BsonType.Int32)
                            {
                                item.before_update = document["modifyBefore"].AsInt32.ToString();
                            }
                            else if (document["modifyBefore"].BsonType == BsonType.Int64)
                            {
                                item.before_update = document["modifyBefore"].AsInt64.ToString();
                            }
                            else if (document["modifyBefore"].BsonType == BsonType.Double)
                            {
                                item.before_update = document["modifyBefore"].AsDouble.ToString();
                            }
                            else
                            {
                                item.before_update = document["modifyBefore"].AsString;
                            }
                        }
                        else
                        {
                            item.before_update = string.Empty;
                        }

                        if (document.Contains("modifyAfter"))
                        {
                            if (document["modifyAfter"].IsBsonNull)
                            {
                                item.after_update = string.Empty;
                            }
                            else if (document["modifyAfter"].BsonType == BsonType.Int32)
                            {
                                item.after_update = document["modifyAfter"].AsInt32.ToString();
                            }
                            else if (document["modifyAfter"].BsonType == BsonType.Int64)
                            {
                                item.after_update = document["modifyAfter"].AsInt64.ToString();
                            }
                            else if (document["modifyAfter"].BsonType == BsonType.Double)
                            {
                                item.after_update = document["modifyAfter"].AsDouble.ToString();
                            }
                            else
                            {
                                item.after_update = document["modifyAfter"].AsString;
                            }
                        }
                        else
                        {
                            item.after_update = string.Empty;
                        }

                        item.update_date = this.ConvertStringToDate(document.Contains("modifyDate") ? (document["modifyDate"].IsBsonNull ? string.Empty : document["modifyDate"].AsInt64.ToString()) : string.Empty);

                        if (string.IsNullOrWhiteSpace(item.update_item) && string.IsNullOrWhiteSpace(item.update_date))
                        {
                            continue;
                        }
                        report.update_records.Add(item);
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
        void LoadAndParseReportStockChange(string responseData, Report report)
        {
            var str = "var stockListJsonStr='";
            if (responseData.Contains(str))
            {
                var tempStr = responseData.Substring(responseData.IndexOf(str) + str.Length);
                tempStr = tempStr.Substring(0, tempStr.IndexOf("';"));
                object[] objs = { new { inv = "", transAmPrBef = "", transAmPrAft = "", altDate = 0 } };
                var list = JsonConvert.DeserializeAnonymousType(tempStr, objs);
                if (list != null && list.Any())
                {
                    foreach (object obj in list)
                    {
                        BsonDocument document = BsonDocument.Parse(obj.ToString());
                        StockChangeItem item = new StockChangeItem();
                        item.seq_no = report.stock_changes.Count + 1;
                        item.name = document.Contains("inv") ? (document["inv"].IsBsonNull ? string.Empty : document["inv"].AsString) : string.Empty;
                        if (document.Contains("transAmPrBef"))
                        {
                            if (document["transAmPrBef"].IsBsonNull)
                            {
                                item.before_percent = string.Empty;
                            }
                            else if (document["transAmPrBef"].BsonType == BsonType.Int32)
                            {
                                item.before_percent = document["transAmPrBef"].AsInt32.ToString();
                            }
                            else if (document["transAmPrBef"].BsonType == BsonType.Int64)
                            {
                                item.before_percent = document["transAmPrBef"].AsInt64.ToString();
                            }
                            else if (document["transAmPrBef"].BsonType == BsonType.Double)
                            {
                                item.before_percent = document["transAmPrBef"].AsDouble.ToString();
                            }
                            else
                            {
                                item.before_percent = document["transAmPrBef"].AsString;
                            }
                        }
                        else
                        {
                            item.before_percent = string.Empty;
                        }

                        if (document.Contains("transAmPrAft"))
                        {
                            if (document["transAmPrAft"].IsBsonNull)
                            {
                                item.after_percent = string.Empty;
                            }
                            else if (document["transAmPrAft"].BsonType == BsonType.Int32)
                            {
                                item.after_percent = document["transAmPrAft"].AsInt32.ToString();
                            }
                            else if (document["transAmPrAft"].BsonType == BsonType.Int64)
                            {
                                item.after_percent = document["transAmPrAft"].AsInt64.ToString();
                            }
                            else if (document["transAmPrAft"].BsonType == BsonType.Double)
                            {
                                item.after_percent = document["transAmPrAft"].AsDouble.ToString();
                            }
                            else
                            {
                                item.after_percent = document["transAmPrAft"].AsString;
                            }
                        }
                        else
                        {
                            item.after_percent = string.Empty;
                        }

                        item.change_date = this.ConvertStringToDate(document.Contains("altDate") ? (document["altDate"].IsBsonNull ? string.Empty : document["altDate"].AsInt64.ToString()) : string.Empty);

                        report.stock_changes.Add(item);
                    }
                }
            }
        }
        #endregion

        #region 解析年报对外提供保证担保信息
        /// <summary>
        /// 解析年报对外提供保证担保信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        void LoadAndParseReportExternalGuarantee(string responseData, Report report)
        {
            var str = "var guaranListJsonStr='";
            if (responseData.Contains(str))
            {
                var tempStr = responseData.Substring(responseData.IndexOf(str) + str.Length);
                tempStr = tempStr.Substring(0, tempStr.IndexOf("';"));
                object[] objs = { new { gaType = "", gaTypeName = "", guaranPeriod = "", guaranPeriodName = "", id = 0, more = "",  
                    mortgagor = "",pefPerForm = 1425312000000 ,pefPerTo=1456934400000,priClaSecAm=0.0,priClaSecKind="",priClaSecKindName="",showSign=""} };
                var list = JsonConvert.DeserializeAnonymousType(tempStr, objs);
                if (list != null && list.Any())
                {
                    foreach (object obj in list)
                    {
                        BsonDocument document = BsonDocument.Parse(obj.ToString());
                        ExternalGuarantee item = new ExternalGuarantee();
                        item.seq_no = report.external_guarantees.Count + 1;
                        item.creditor = document.Contains("more") ? (document["more"].IsBsonNull ? string.Empty : document["more"].AsString) : string.Empty;
                        item.debtor = document.Contains("mortgagor") ? (document["mortgagor"].IsBsonNull ? string.Empty : document["mortgagor"].AsString) : string.Empty;
                        item.type = document.Contains("priClaSecKindName") ? (document["priClaSecKindName"].IsBsonNull ? string.Empty : document["priClaSecKindName"].AsString) : string.Empty;
                        if (document.Contains("priClaSecAm"))
                        {
                            if (document["priClaSecAm"].IsBsonNull)
                            {
                                item.amount = string.Empty;
                            }
                            else if (document["priClaSecAm"].BsonType == BsonType.Int32)
                            {
                                item.amount = document["priClaSecAm"].AsInt32.ToString() + "万元";
                            }
                            else if (document["priClaSecAm"].BsonType == BsonType.Int64)
                            {
                                item.amount = document["priClaSecAm"].AsInt64.ToString() + "万元";
                            }
                            else if (document["priClaSecAm"].BsonType == BsonType.Double)
                            {
                                item.amount = document["priClaSecAm"].AsDouble.ToString() + "万元";
                            }
                        }
                        else
                        {
                            item.amount = string.Empty;
                        }

                        var dateFrom = this.ConvertStringToDate(document.Contains("pefPerForm") ? (document["pefPerForm"].IsBsonNull ? string.Empty : document["pefPerForm"].AsInt64.ToString()) : string.Empty);
                        var dateTo = this.ConvertStringToDate(document.Contains("pefPerTo") ? (document["pefPerTo"].IsBsonNull ? string.Empty : document["pefPerTo"].AsInt64.ToString()) : string.Empty);
                        item.period = string.Format("{0}-{1}", dateFrom, dateTo);
                        item.guarantee_time = document.Contains("guaranPeriodName") ? (document["guaranPeriodName"].IsBsonNull ? string.Empty : document["guaranPeriodName"].AsString) : string.Empty;
                        item.guarantee_type = document.Contains("gaTypeName") ? (document["gaTypeName"].IsBsonNull ? string.Empty : document["gaTypeName"].AsString) : string.Empty;

                        report.external_guarantees.Add(item);
                    }
                }
            }
        }
        #endregion

        #region 解析年报股东信息
        void LoadAndParseReportPartner(string responseData, Report report)
        {
            var str = "var listJsonStr='";
            if (responseData.Contains(str))
            {

                var tempStr = responseData.Substring(responseData.IndexOf(str) + str.Length);

                tempStr = tempStr.Substring(0, tempStr.IndexOf("';"));
                object[] objs = { new { inv = "", subConAm = "", conDate = "", conFormName = "", acConAm = "", realConDate = "", realConFormName = "" } };
                var list = JsonConvert.DeserializeAnonymousType(tempStr, objs);
                if (list != null && list.Any())
                {
                    foreach (object obj in list)
                    {
                        BsonDocument item = BsonDocument.Parse(obj.ToString());
                        Partner partner = new Partner();
                        partner.seq_no = report.partners.Count + 1;
                        partner.stock_name = item["inv"].IsBsonNull ? string.Empty : item["inv"].AsString;
                        partner.real_capi_items = new List<RealCapiItem>();
                        partner.should_capi_items = new List<ShouldCapiItem>();

                        ShouldCapiItem sItem = new ShouldCapiItem();
                        if (item["subConAm"].IsBsonNull)
                        {
                            sItem.shoud_capi = string.Empty;
                        }
                        else if (item["subConAm"].BsonType == BsonType.Int32)
                        {
                            sItem.shoud_capi = item["subConAm"].AsInt32.ToString();
                        }
                        else if (item["subConAm"].BsonType == BsonType.Int64)
                        {
                            sItem.shoud_capi = item["subConAm"].AsInt64.ToString();
                        }
                        else if (item["subConAm"].BsonType == BsonType.Double)
                        {
                            sItem.shoud_capi = item["subConAm"].AsDouble.ToString();
                        }

                        sItem.should_capi_date = item.Contains("conDate") ? (item["conDate"].IsBsonNull ? string.Empty : item["conDate"].AsInt64.ToString()) : string.Empty;
                        sItem.should_capi_date = this.ConvertStringToDate(sItem.should_capi_date);
                        sItem.invest_type = item.Contains("conFormName") ? (item["conFormName"].IsBsonNull ? string.Empty : item["conFormName"].AsString) : string.Empty;
                        partner.should_capi_items.Add(sItem);

                        RealCapiItem rItem = new RealCapiItem();
                        if (item["acConAm"].IsBsonNull)
                        {
                            rItem.real_capi = string.Empty;
                        }
                        else if (item["acConAm"].BsonType == BsonType.Int32)
                        {
                            rItem.real_capi = item["acConAm"].AsInt32.ToString();
                        }
                        else if (item["acConAm"].BsonType == BsonType.Int64)
                        {
                            rItem.real_capi = item["acConAm"].AsInt64.ToString();

                        }
                        else if (item["acConAm"].BsonType == BsonType.Double)
                        {
                            rItem.real_capi = item["acConAm"].AsDouble.ToString();
                        }

                        rItem.real_capi_date = item.Contains("realConDate") ? (item["realConDate"].IsBsonNull ? string.Empty : item["realConDate"].AsInt64.ToString()) : string.Empty;
                        rItem.real_capi_date = this.ConvertStringToDate(rItem.real_capi_date);
                        rItem.invest_type = item.Contains("realConFormName") ? (item["realConFormName"].IsBsonNull ? string.Empty : item["realConFormName"].AsString) : string.Empty;
                        partner.real_capi_items.Add(rItem);

                        report.partners.Add(partner);
                    }

                }
            }


            //List<Partner> partnerList = new List<Partner>();
            //if (trList != null && trList.Any())
            //{
            //    int j = 1;
            //    foreach (HtmlNode rowNode in trList)
            //    {
            //        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
            //        if (tdList != null && tdList.Count > 5 && tdList[0].InnerText.Trim() != "")
            //        {
            //            Partner item = new Partner();

            //            item.seq_no = j++;
            //            item.stock_name = tdList[1].InnerText.Trim();
            //            item.stock_type = "";
            //            item.identify_no = "";
            //            item.identify_type = "";
            //            item.stock_percent = "";
            //            item.ex_id = "";
            //            item.real_capi_items = new List<RealCapiItem>();
            //            item.should_capi_items = new List<ShouldCapiItem>();

            //            ShouldCapiItem sItem = new ShouldCapiItem();
            //            var sCapi = tdList[2].InnerText.Trim();
            //            sItem.shoud_capi = string.IsNullOrEmpty(sCapi) ? "" : sCapi;
            //            sItem.should_capi_date = tdList[3].InnerText.Trim();
            //            sItem.invest_type = tdList[4].InnerText.Trim();
            //            item.should_capi_items.Add(sItem);

            //            RealCapiItem rItem = new RealCapiItem();
            //            var rCapi = tdList[5].InnerText.Trim();
            //            rItem.real_capi = string.IsNullOrEmpty(rCapi) ? "" : rCapi;
            //            rItem.real_capi_date = tdList[6].InnerText.Trim();
            //            rItem.invest_type = tdList[7].InnerText.Trim();
            //            item.real_capi_items.Add(rItem);

            //            partnerList.Add(item);
            //        }
            //    }
            //}


        }
        #endregion

        #region 行政处罚解析
        /// <summary>
        /// 行政处罚解析
        /// </summary>
        /// <param name="_enterpriseInfo"></param>
        /// <param name="rootNode"></param>
        private void LoadAndParsePunishment()
        {
            Random ran = new Random();
            #region 行政处罚信息分页
            //行政处罚信息分页
            List<AdministrativePunishment> administrativePunishments = new List<AdministrativePunishment>();
            var request = CreateRequest();
            List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("punishment"));
            if (responseList.Count > 0)
            {
                this.LoadAndParsePunishPage(responseList[0].Data, administrativePunishments);
                request.AddOrUpdateRequestParameter("pno", "1");
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(responseList[0].Data);
                HtmlNode rNode = doc.DocumentNode;
                var pageNode = rNode.SelectSingleNode("//div[@class='ax_image fenye']");
                var tables = rNode.SelectNodes("//table");
                if (tables != null && tables.Count > 1 && pageNode != null)
                {
                    int page = int.Parse(pageNode.SelectNodes("./ul/li")[1].InnerText.Replace("共", "").Replace("页", "").Replace("&nbsp;", ""));
                    for (int index = 2; index <= page; index++)
                    {
                        request.AddOrUpdateRequestParameter("pno", index.ToString());
                        request.AddOrUpdateRequestParameter("ran", ran.NextDouble().ToString());
                        responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("punishment_page"));
                        if (responseList != null && responseList.Any())
                        {
                            this.LoadAndParsePunishPage(responseList.First().Data,administrativePunishments);
                        }
                    }
                }
                _enterpriseInfo.administrative_punishments = administrativePunishments;
            }
            #endregion
        }
        #endregion

        #region 解析行政处罚信息分页及详细
        /// <summary>
        /// 解析行政处罚信息的分页
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParsePunishPage(string responseData, List<AdministrativePunishment> punishList)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var table = rootNode.SelectSingleNode("//table[@id='punTab']");
            if (table != null)
            {
                HtmlNodeCollection trList = table.SelectNodes("./tr");
                if (trList != null)
                {
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");

                        if (tdList != null && tdList.Count > 6)
                        {
                            AdministrativePunishment punish = new AdministrativePunishment();
                            punish.seq_no = punishList.Count + 1;
                            punish.number = tdList[1].InnerText.Trim();
                            punish.date = tdList[5].InnerText.Trim();
                            punish.department = tdList[4].InnerText.Trim();
                            punish.illegal_type = tdList[2].InnerText.Trim();
                            punish.content = tdList[3].InnerText.Replace("<!---->", "").Replace("\r", "").Replace("\n", "").Trim();
                            punish.oper_name = _enterpriseInfo.oper_name;
                            punish.reg_no = _enterpriseInfo.reg_no;
                            punish.name = _enterpriseInfo.name;
                            punish.public_date = tdList[6].InnerText.Trim();
                            punishList.Add(punish);
                            var dtl = tdList.Last().SelectSingleNode("./a");
                            if (dtl != null)
                            {
                                var text = dtl.Attributes["onclick"].Value;
                                var match = Regex.Match(text, "window.open\\(.*?\\)");
                                if (match != null && match.Success && !string.IsNullOrEmpty(match.Value))
                                {
                                    string id = match.Value.Replace("window.open(", "").Replace(")", "").Replace("'", "");
                                    // 解析行政处罚信息详情
                                    if (!string.IsNullOrEmpty(id))
                                    {
                                        var request = CreateRequest();
                                        List<RequestSetting> elements = new List<RequestSetting>();
                                        elements.Add(new RequestSetting("get",
                                            "http://sx.gsxt.gov.cn" + id, string.Empty, "0", "punishmentdetail"));
                                        List<ResponseInfo> reponseList = request.GetResponseInfo(elements);
                                        if (reponseList.Count() > 0)
                                        {
                                            LoadAndParsePunishDetails(punish, reponseList[0].Data);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
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
            var divs = rootNode.SelectNodes("//div[@class='guidedivall']/div[@class='guidedivmin']/div");
            if (divs != null)
                punish.description = divs.Last().InnerHtml;
        }
        #endregion

        #region 构建请求
        /// <summary>
        /// 构建请求
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

        #region 解析股权冻结
        /// <summary>
        /// 解析股权冻结
        /// </summary>
        /// <param name="responseInfo"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseJudicial(string responseInfo, EnterpriseInfo _enterpriseInfo)
        {
            Random ran = new Random();
            List<JudicialFreeze> judicialFreezeList = new List<JudicialFreeze>();
            var request = CreateRequest();
            request.AddOrUpdateRequestParameter("pno", "1");
            request.AddOrUpdateRequestParameter("ran", ran.NextDouble().ToString());
            List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("judicial_freeze"));
            if (responseList.Count > 0)
            {
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(responseList[0].Data);
                HtmlNode rNode = doc.DocumentNode;
                var pageNode = rNode.SelectSingleNode("//div[@class='ax_image fenye']");
                var tables = rNode.SelectNodes("//table");
                if (tables != null && tables.Count > 1)
                {
                    var table = tables[1];
                    this.LoadAndParseFreezePage(1, table.OuterHtml);
                    int page = int.Parse(pageNode.FirstChild.ChildNodes[1].InnerText.Replace("共", "").Replace("页", "").Replace("&nbsp;", ""));
                    for (int index = 2; index <= page; index++)
                    {
                        request.AddOrUpdateRequestParameter("ran", ran.NextDouble().ToString());
                        request.AddOrUpdateRequestParameter("pno", index.ToString());
                        responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("judicial_freeze"));
                        doc = new HtmlDocument();
                        doc.LoadHtml(responseList[0].Data);
                        rNode = doc.DocumentNode; tables = rNode.SelectNodes("//table");
                        if (tables.Count > 1)
                        {
                            table = tables[1];
                            this.LoadAndParseFreezePage(index, table.OuterHtml);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析股权冻结分页
        void LoadAndParseFreezePage(int page, string responseData)
        {
            int seqno = (page - 1) * 5;
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            HtmlNodeCollection trList = rootNode.SelectNodes("//tr");

            foreach (HtmlNode rowNode in trList)
            {
                HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                if (tdList != null && tdList.Count == 7)
                {
                    JudicialFreeze jf = new JudicialFreeze();
                    jf.seq_no = _enterpriseInfo.judicial_freezes.Count + 1;
                    jf.be_executed_person = tdList[1].InnerText;
                    jf.amount = tdList[2].InnerText;
                    jf.executive_court = tdList[3].InnerText;
                    jf.number = tdList[4].InnerText;
                    jf.status = tdList[5].InnerText.Replace("&nbsp;", "");
                    jf.type = "股权冻结";
                    var aNode = tdList.Last().SelectSingleNode("./a");
                    if (aNode != null && aNode.Attributes.Contains("onclick"))
                    {
                        var onclick = aNode.Attributes["onclick"].Value;
                        var arr = onclick.Split(',');
                        var request = this.CreateRequest();
                        var category = 0;
                        var responseList = new List<ResponseInfo>();
                        if (arr[0].StartsWith("seeJudicialinfo"))
                        {
                            category = 1;
                        }
                        if (category == 0)
                        {
                            request.AddOrUpdateRequestParameter("frozState", arr[1].Trim(new char[] { '\'' }));
                            request.AddOrUpdateRequestParameter("freezeId", arr[2].Trim(new char[] { '\'' }));
                            request.AddOrUpdateRequestParameter("bodyid", arr.Last().TrimStart(new char[] { '\'' }).Replace("\')", ""));
                            responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("judicial_freeze_detail"));
                        }
                        else
                        {
                            request.AddOrUpdateRequestParameter("shareholderChangeId", arr[0].Split('\'')[1]);
                            responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("judicial_freeze_detail_stockchange"));
                        }
                        if (responseList != null && responseList.Any())
                        {
                            var inner_document = new HtmlDocument();
                            inner_document.LoadHtml(responseList.First().Data);
                            HtmlNode inner_rootNode = inner_document.DocumentNode;
                            var inner_tables = inner_rootNode.SelectNodes("//table");

                            if (inner_tables != null && inner_tables.Any())
                            {
                                foreach (var table in inner_tables)
                                {
                                    var div = table.SelectSingleNode("./preceding-sibling::div[1]");
                                    if (div.InnerText.Contains("股权冻结信息"))
                                    {
                                        this.LoadAndParseFreezeDetail(jf, table.SelectNodes("./tr"));
                                    }
                                    else if (div.InnerText.Contains("股权解冻信息"))
                                    {
                                        this.LoadAndParseUnFreezeDetail(jf, table.SelectNodes("./tr"));
                                    }
                                    else if (div.InnerText.Contains("股权续行冻结信息") || div.InnerText.Contains("续行冻结信息") || div.InnerText.Contains("股权续行冻结"))
                                    {
                                        this.LoadAndParseContinueFreeze(jf, table.SelectNodes("./tr"));
                                    }
                                    else if (div.InnerText.Contains("股东变更信息") || div.InnerText.Contains("股权变更信息"))
                                    {
                                        jf.type = "股权变更";
                                        this.LoadAndParsePartnerChangeFreeze(jf, table.SelectNodes("./tr"));
                                    }
                                }
                            }
                        }
                    }
                    _enterpriseInfo.judicial_freezes.Add(jf);
                }

            }
        }
        #endregion

        #region 解析股权冻结详情
        /// <summary>
        /// 解析股权冻结详情
        /// </summary>
        /// <param name="item"></param>
        /// <param name="trList"></param>
        private void LoadAndParseFreezeDetail(JudicialFreeze item, HtmlNodeCollection trList)
        {
            if (trList != null && trList.Count > 1)
            {
                JudicialFreezeDetail freeze = new JudicialFreezeDetail();
                for (int i = 0; i < trList.Count; i++)
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
                                case "被执行人持有股权、其它投资权益的数额":
                                    freeze.freeze_amount = tdList[j].InnerText.Trim();
                                    break;
                                case "被执行人证件种类":
                                case "被执行人证照种类":
                                    freeze.assist_ident_type = tdList[j].InnerText.Trim();
                                    break;
                                case "被执行人证件号码":
                                case "被执行人证照号码":
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
        #endregion

        #region 解析股权冻结详情--解冻
        void LoadAndParseUnFreezeDetail(JudicialFreeze item, HtmlNodeCollection trList)
        {
            if (trList != null && trList.Count > 1)
            {
                JudicialUnFreezeDetail unfreeze = new JudicialUnFreezeDetail();
                for (int i = 0; i < trList.Count; i++)
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
                                case "被执行人证照种类":
                                    unfreeze.assist_ident_type = tdList[j].InnerText.Trim();
                                    break;
                                case "被执行人证件号码":
                                case "被执行人证照号码":
                                    unfreeze.assist_ident_no = tdList[j].InnerText.Trim();
                                    break;
                                case "解除冻结日期":
                                    unfreeze.unfreeze_date = tdList[j].InnerText.Trim();
                                    break;
                                case "公示日期":
                                    unfreeze.public_date = tdList[j].InnerText.Trim();
                                    break;
                            }
                        }
                    }
                }
                item.un_freeze_detail = unfreeze;
                item.un_freeze_details.Add(unfreeze);
            }
        }
        #endregion

        #region 解析股权冻结-续行冻结
        /// <summary>
        /// 解析股权冻结-续行冻结
        /// </summary>
        /// <param name="item"></param>
        /// <param name="trList"></param>
        void LoadAndParseContinueFreeze(JudicialFreeze item, HtmlNodeCollection trList)
        {
            if (trList != null && trList.Count > 1)
            {
                JudicialContinueFreezeDetail freeze = new JudicialContinueFreezeDetail();
                for (int i = 0; i < trList.Count; i++)
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
                                case "被执行人持有股权、其它投资权益的数额":
                                    freeze.freeze_amount = tdList[j].InnerText.Trim();
                                    break;
                                case "被执行人证件种类":
                                case "被执行人证照种类":
                                    freeze.assist_ident_type = tdList[j].InnerText.Trim();
                                    break;
                                case "被执行人证件号码":
                                case "被执行人证照号码":
                                    freeze.assist_ident_no = tdList[j].InnerText.Trim();
                                    break;
                                case "续行冻结期限自":
                                    var th = tdList[j].SelectSingleNode("./th");
                                    var td = tdList[j].SelectSingleNode("./td");
                                    if (th != null && td != null && th.InnerText.Contains("续行冻结期限至"))
                                    {
                                        freeze.continue_date_start = tdList[j].InnerText.Replace(th.InnerText, "").Replace(td.InnerText, "").Trim();
                                        freeze.continue_date_end = td.InnerText.Trim();
                                    }
                                    else
                                    {
                                        freeze.continue_date_start = tdList[j].InnerText.Trim();
                                    }
                                    break;
                                case "续行冻结期限至":
                                    if (string.IsNullOrWhiteSpace(freeze.continue_date_end))
                                    {
                                        freeze.continue_date_end = tdList[j].InnerText.Trim();
                                    }
                                    else
                                    {
                                        freeze.continue_date_peroid = tdList[j].InnerText.Trim();
                                    }
                                    break;
                                case "公示日期":
                                    freeze.public_date = tdList[j].InnerText.Trim();
                                    break;
                            }
                        }
                    }
                }
                item.continue_freeze_details.Add(freeze);
            }
        }
        #endregion

        #region 解析股权冻结-股权变更
        /// <summary>
        /// 解析股权冻结详情
        /// </summary>
        /// <param name="item"></param>
        /// <param name="div"></param>
        private void LoadAndParsePartnerChangeFreeze(JudicialFreeze item, HtmlNodeCollection trList)
        {
            if (trList != null && trList.Count > 1)
            {
                JudicialFreezePartnerChangeDetail freeze = new JudicialFreezePartnerChangeDetail();
                for (int i = 0; i < trList.Count; i++)
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
                                case "被执行人持有股权、其它投资权益的数额":
                                case "被执行人持有股权数额":
                                    freeze.freeze_amount = tdList[j].InnerText.Trim();
                                    break;
                                case "被执行人证件种类":
                                case "被执行人证照种类":
                                    freeze.assist_ident_type = tdList[j].InnerText.Trim();
                                    break;
                                case "被执行人证件号码":
                                case "被执行人证照号码":
                                    freeze.assist_ident_no = tdList[j].InnerText.Trim();
                                    break;
                                case "受让人":
                                    freeze.assignee = tdList[j].InnerText.Trim();
                                    break;
                                case "协助执行日期":
                                    freeze.xz_execute_date = tdList[j].InnerText.Trim();
                                    break;
                                case "受让人证件种类":
                                case "受让人证照种类":
                                    freeze.assignee_ident_type = tdList[j].InnerText.Trim();
                                    break;
                                case "受让人证件号码":
                                case "受让人证照号码":
                                    freeze.assignee_ident_no = tdList[j].InnerText.Trim();
                                    break;
                            }
                        }
                    }
                }
                item.pc_freeze_detail = freeze;
            }
        }

        #endregion
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
    }
}