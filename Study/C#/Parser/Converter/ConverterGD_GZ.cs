using HtmlAgilityPack;
using iOubo.iSpider.Common;
using iOubo.iSpider.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace iOubo.iSpider.Infrastructure.Parser
{
    public class ConverterGD_GZ : IConverter
    {
        DataRequest _request;
        RequestInfo _requestInfo;
        RequestXml _requestXml;
        EnterpriseInfo _enterpriseInfo = new EnterpriseInfo();
        List<AbnormalInfo> _abnormals = new List<AbnormalInfo>();
        List<CheckupInfo> _checkups = new List<CheckupInfo>();

        public SummaryEntity ProcessRequestAndParse(RequestInfo requestInfo)
        {
            var province = "GD_GZ_City";
            this._requestInfo = requestInfo;
            this._request = new DataRequest(this._requestInfo);
            this._requestXml = new RequestXml(this._requestInfo.CurrentPath, province);
            InitialEnterpriseInfo();

            List<ResponseInfo> responseList = GetResponseInfo(_requestXml.GetRequestListByGroup("gongshang"));
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
                    case "Detail":
                        LoadAndParseBasicInfo(responseInfo.Data);
                        break;
                    case "Shareholder"://股东信息
                        LoadAndParsePartners(responseInfo.Data);
                        break;
                    case "Staff"://主要人员
                        LoadAndParseEmployee(responseInfo.Data);
                        break;
                    case "EquityPledge"://股权出质
                        LoadAndParseGQCZ(responseInfo.Data);
                        break;
                    case "ChattelMortgage"://动产抵押
                        LoadAndParseDCDY(responseInfo.Data);
                        break;
                    case "AnnualReport"://年报信息
                        LoadAndParseReport(responseInfo.Data);
                        break;
                    case "Approval"://行政许可
                        LoadAndParseLicenses0(responseInfo.Data);
                        break;
                    case "Punishment"://行政处罚
                        LoadAndParseAdministrativePunishment(responseInfo.Data);
                        break;
                    case "SelfApproval"://行政许可
                        LoadAndParseLicenses(responseInfo.Data);
                        break;
                    case "IntellectualProperty"://知识产权
                        break;
                    case "Investment"://股东及出资
                        LoadAndParseGDJCZ(responseInfo.Data);
                        break;
                    default:
                        break;
                }
            }
        }
        #endregion

        #region 解析加载基本信息
        /// <summary>
        /// 解析加载基本信息
        /// </summary>
        /// <param name="responseData"></param>
        private void LoadAndParseBasicInfo(string responseData)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var div = rootNode.SelectSingleNode("//div[@id='BasicInfo']");
            var table = rootNode.SelectSingleNode("//div[@id='BasicInfo']/following-sibling::div[1]/div/table");
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null)
                {
                    foreach (HtmlNode tr in trs)
                    {
                        var title = tr.SelectSingleNode("./th").InnerText.Replace("\r\n","").Replace(" ","");
                        var content = tr.SelectSingleNode("./td").InnerText.Replace("\r\n", "").Replace(" ", "");
                        switch (title)
                        {
                            case "注册号":
                                content = tr.SelectSingleNode("./td").SelectSingleNode("span").InnerText;
                                _enterpriseInfo.reg_no = content;
                                break;
                            case "名称":
                                _enterpriseInfo.name = content.Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                                break;
                            case "法定代表人":
                                _enterpriseInfo.oper_name = content;
                                break;
                            case "社会信用代码":
                                _enterpriseInfo.credit_no = content;
                                break;
                            case "主营项目类别":
                                _enterpriseInfo.domains.Add(content);
                                break;
                            case "经营范围":
                                tr.SelectSingleNode("./td").SelectSingleNode("span").Remove();
                                content = tr.SelectSingleNode("./td").InnerText;
                                _enterpriseInfo.scope = content;
                                break;
                            case "住所(经营场所)":
                                Address address = new Address();
                                address.name = "注册地址";
                                content = tr.SelectSingleNode("./td").SelectSingleNode("span").InnerText;
                                address.address = content;
                                address.postcode = "";
                                _enterpriseInfo.addresses.Add(address);
                                break;
                            case "认缴注册资本":
                                _enterpriseInfo.regist_capi = content;
                                break;
                            case "商事主体类型":
                                _enterpriseInfo.econ_kind = content;
                                break;
                            case "成立日期":
                                _enterpriseInfo.start_date = content;
                                break;
                            case "营业期限":
                                _enterpriseInfo.term_start = content;
                                break;
                            case "核发日期":
                                _enterpriseInfo.check_date = content;
                                break;
                            case "登记机关":
                                _enterpriseInfo.belong_org = content;
                                break;
                            case "状态":
                                _enterpriseInfo.status = content;
                                break;
                            default:
                                break;
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析加载股东信息
        /// <summary>
        /// 解析加载股东信息
        /// </summary>
        /// <param name="responseData"></param>
        private void LoadAndParsePartners(string responseData)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            var tbody = rootNode.SelectSingleNode("./table/tbody");
            if (tbody != null)
            {
                var trs = tbody.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    foreach (HtmlNode tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Count() > 1)
                        {
                            Partner partner = new Partner();
                            partner.seq_no = _enterpriseInfo.partners.Count + 1;
                            partner.stock_name = tds[0].InnerText;
                            partner.stock_type = tds[1].InnerText;
                            _enterpriseInfo.partners.Add(partner);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析加载主要成员
        /// <summary>
        /// 解析加载主要成员
        /// </summary>
        /// <param name="responseData"></param>
        private void LoadAndParseEmployee(string responseData)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            var tbody = rootNode.SelectSingleNode("./table/tbody");
            if (tbody != null)
            {
                var trs = tbody.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    foreach (HtmlNode tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Count() > 1)
                        {
                            Employee item = new Employee();
                            item.seq_no = _enterpriseInfo.employees.Count + 1;
                            item.name = tds[0].InnerText;
                            item.job_title = tds[1].InnerText;
                            _enterpriseInfo.employees.Add(item);
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
        /// <param name="responseData"></param>
        private void LoadAndParseGQCZ(string responseData)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            var tbody = rootNode.SelectSingleNode("./table/tbody");
            if (tbody != null)
            {
                var trs = tbody.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    foreach (HtmlNode tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Count() > 6)
                        {
                            EquityQuality item = new EquityQuality();
                            item.seq_no = _enterpriseInfo.equity_qualities.Count + 1;
                            item.number = tds[0].InnerText;
                            item.pledgor = tds[1].InnerText;
                            item.pledgor_identify_no = tds[2].InnerText;
                            item.pledgor_amount = tds[3].InnerText;
                            item.pawnee = tds[4].InnerText;
                            item.date = tds[5].InnerText;
                            item.status = tds[6].InnerText;
                            _enterpriseInfo.equity_qualities.Add(item);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析动产抵押
        private void LoadAndParseDCDY(string responseData)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            var tbody = rootNode.SelectSingleNode("./table/tbody");
            if (tbody != null)
            {
                var trs = tbody.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    foreach (HtmlNode tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Count() > 4)
                        {
                            MortgageInfo item = new MortgageInfo();
                           
                            item.seq_no = _enterpriseInfo.mortgages.Count + 1;
                            item.number = tds[0].InnerText;
                            item.date = tds[1].InnerText;
                            item.department = tds[2].InnerText;
                            item.amount= tds[3].InnerText;
                            var a = tds[4].SelectSingleNode("./a");
                            if (a != null)
                            {
                                var href = a.Attributes["href"] == null ? a.Attributes["href"].Value : string.Empty;
                                if (!string.IsNullOrWhiteSpace(href))
                                {
                                    var url = string.Format("{0}{1}", "http://cri.gz.gov.cn",href);
                                    RequestHandler request = new RequestHandler();
                                    var response = request.HttpGet(url, "");
                                    LoadAndParseDCDYDetail(response,item);
                                }
                            }
                            _enterpriseInfo.mortgages.Add(item);
                        }
                    }
                }
            }
            
        }
        #endregion

        #region 解析加载动产抵押详情
        private void LoadAndParseDCDYDetail(string responseData, MortgageInfo item)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            var divs = rootNode.SelectNodes("//div[@class='heading heading-v1']");
            if (divs != null)
            {
                foreach (HtmlNode div in divs)
                {
                    var title = div.InnerText.Replace("\r\n", "");
                    var table = div.SelectSingleNode("./following-sibling::table[1]");
                    if (title.Contains("动产抵押登记信息"))
                    {
                        var trs = table.SelectNodes("./tr");
                        if (trs != null && trs.Any())
                        {
                            foreach (HtmlNode tr in trs)
                            {
                                var th = tr.SelectSingleNode("./th");
                                var td = tr.SelectSingleNode("./td");
                                switch (th.InnerText)
                                {
                                    case "登记编号":
                                        item.number = td.InnerText;
                                        break;
                                    case "登记日期":
                                        item.date = td.InnerText;
                                        break;
                                    case "登记机关":
                                        item.department = td.InnerText;
                                        break;
                                    case "被担保债权种类":
                                        item.type = td.InnerText;
                                        break;
                                    case "被担保债权数额":
                                        item.amount = td.InnerText;
                                        break;
                                    case "债务人履行债务的期限":
                                        item.period = td.InnerText;
                                        break;
                                    case "担保范围":
                                        item.scope = td.InnerText;
                                        break;
                                    case "备注":
                                        item.remarks = td.InnerText;
                                        break;
                                    default:
                                        break;
                                }
                            }
                        }
                    }
                    else if (title.Contains("抵押权人概况"))
                    {
                        var dyrTrs = table.SelectNodes("./tbody/tr");
                        if (dyrTrs != null && dyrTrs.Any())
                        {
                            foreach (HtmlNode dyrTr in dyrTrs)
                            {
                                var dyrTds = dyrTr.SelectNodes("./td");
                                if (dyrTds != null && dyrTds.Count > 2)
                                {
                                    Mortgagee mortgagee = new Mortgagee();
                                    mortgagee.seq_no = item.mortgagees.Count + 1;
                                    mortgagee.name = dyrTds[0].InnerText;
                                    mortgagee.identify_type = dyrTds[1].InnerText;
                                    mortgagee.identify_no = dyrTds[2].InnerText;
                                    item.mortgagees.Add(mortgagee);
                                }
                            }
                        }
                    }
                    else if (title.Contains("抵押物概况"))
                    {
                        var dyrTrs = table.SelectNodes("./tbody/tr");
                        if (dyrTrs != null && dyrTrs.Any())
                        {
                            foreach (HtmlNode dyrTr in dyrTrs)
                            {
                                var dyrTds = dyrTr.SelectNodes("./td");
                                if (dyrTds != null && dyrTds.Count > 3)
                                {
                                    Guarantee guarantee = new Guarantee();
                                    guarantee.seq_no = item.mortgagees.Count + 1;
                                    guarantee.name = dyrTds[0].InnerText;
                                    guarantee.belong_to = dyrTds[1].InnerText;
                                    guarantee.desc = dyrTds[2].InnerText;
                                    guarantee.remarks = dyrTds[3].InnerText;
                                    item.guarantees.Add(guarantee);
                                }
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析年报
        /// <summary>
        /// 解析年报
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseReport(string responseData)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            var tables = rootNode.SelectNodes("//table");
            if (tables != null)
            {
                foreach (HtmlNode table in tables)
                {
                    var trs = table.SelectNodes("./tbody/tr");
                    if (trs != null)
                    {
                        foreach (HtmlNode tr in trs)
                        {
                            var tds = tr.SelectNodes("./td");
                            if (tds != null)
                            {
                                Report report = new Report();
                                if (tds.Count.Equals(3))
                                {
                                    report.report_name = tds[0].InnerText;
                                    report.report_date = tds[1].InnerText;
                                    report.report_year = tds[0].InnerText.Substring(0,4);
                                    var a = tds[2].SelectSingleNode("./a");
                                    if (a != null)
                                    {
                                        var href = a.Attributes["href"].Value;
                                        var url = string.Format("{0}{1}", "http://cri.gz.gov.cn", href);
                                        RequestHandler request = new RequestHandler();
                                        var response = request.HttpGet(url, "");
                                        LoadAndParseReportDetail(response, report);
                                    }
                                }
                                else
                                {
                                    report.report_year = tds[0].InnerText;
                                    report.report_name = tds[1].InnerText;
                                    report.report_date = tds[2].InnerText;
                                    
                                }
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析加载年报详情
        /// <summary>
        /// 解析加载年报详情
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        void LoadAndParseReportDetail(string responseData,Report report)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            var div = rootNode.SelectSingleNode("//div[@class='heading heading-v1']");
            if (div != null)
            {
                var table = div.SelectSingleNode("./following-sibling::table[1]");
                if (table != null)
                {
                    var trs = table.SelectNodes("./tr");
                    if (trs != null && trs.Any())
                    {
                        foreach (HtmlNode tr in trs)
                        {
                            var th = tr.SelectSingleNode("./th");
                            var td = tr.SelectSingleNode("./td");
                            switch (th.InnerText)
                            {
                                case "注册号":
                                    report.reg_no = td.InnerText;
                                    break;
                                case "企业名称":
                                    report.name = td.InnerText.Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                                    break;
                                case "从业人数":
                                    report.collegues_num = td.InnerText;
                                    break;
                                case "企业联系电话":
                                    report.telephone = td.InnerText;
                                    break;
                                case "企业通信地址":
                                    report.address = td.InnerText;
                                    break;
                                case "邮政编码":
                                    report.zip_code = td.InnerText;
                                    break;
                                case "电子邮箱":
                                    report.email = td.InnerText;
                                    break;
                                case "经营状态":
                                    report.status = td.InnerText;
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

        #region 解析加载行政许可信息
        /// <summary>
        /// 解析加载行政许可信息
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseLicenses(string responseData)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            var tbody = rootNode.SelectSingleNode("./table/tbody");
            if (tbody != null)
            {
                var trs = tbody.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    foreach (HtmlNode tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Count() > 5)
                        {
                            LicenseInfo item = new LicenseInfo();
                            item.seq_no = _enterpriseInfo.licenses.Count + 1;
                            item.number = tds[0].InnerText;
                            item.name = tds[1].InnerText;
                            item.start_date = tds[2].InnerText;
                            item.end_date = tds[3].InnerText;
                            item.department = tds[4].InnerText;
                            item.content = tds[5].InnerText;
                            _enterpriseInfo.licenses.Add(item);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析加载行政许可信息
        /// <summary>
        /// 解析加载行政许可信息
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseLicenses0(string responseData)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            var tbody = rootNode.SelectSingleNode("./table/tbody");
            if (tbody != null)
            {
                var trs = tbody.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    foreach (HtmlNode tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Count()>5)
                        {
                            LicenseInfo item = new LicenseInfo();
                            item.seq_no = _enterpriseInfo.licenses.Count + 1;
                            item.number = tds[0].InnerText;
                            item.name = tds[1].InnerText;
                            item.content = tds[2].InnerText;
                            item.department = tds[3].InnerText;
                            item.start_date = tds[4].InnerText;
                            item.end_date = tds[5].InnerText;
                            _enterpriseInfo.licenses.Add(item);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析加载行政处罚
        /// <summary>
        /// 解析加载行政处罚
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseAdministrativePunishment(string responseData)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            var tbody = rootNode.SelectSingleNode("./table/tbody");
            if (tbody != null)
            {
                var trs = tbody.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    foreach (HtmlNode tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Count() > 6)
                        {
                            AdministrativePunishment item = new AdministrativePunishment();
                            item.seq_no = _enterpriseInfo.administrative_punishments.Count + 1;
                            item.number = tds[0].InnerText;
                            item.name = tds[1].InnerText;
                            item.illegal_type = tds[2].InnerText;
                            item.based_on = tds[3].InnerText;
                            item.content = tds[4].InnerText;
                            item.department = tds[5].InnerText;
                            item.date = tds[6].InnerText;
                            _enterpriseInfo.administrative_punishments.Add(item);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析加载股东及出资
        /// <summary>
        /// 解析加载股东及出资
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseGDJCZ(string responseData)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            var tbody = rootNode.SelectSingleNode("./table/tbody");
            if (tbody != null)
            {
                var trs = tbody.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    foreach (HtmlNode tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Count() > 3)
                        {
                            FinancialContribution item = new FinancialContribution();
                            item.seq_no = _enterpriseInfo.financial_contributions.Count + 1;
                            item.investor_name = tds[0].InnerText;
                            item.total_real_capi = tds[1].InnerText;
                            item.total_should_capi = tds[2].InnerText;
                            item.investor_type = tds[3].InnerText;
                            _enterpriseInfo.financial_contributions.Add(item);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析加载知识产权
        void LoadAndParseZSCQ(string responseData)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            var tbody = rootNode.SelectSingleNode("./table/tbody");
            if (tbody != null)
            {
                var trs = tbody.SelectNodes("./tr");
                if (trs != null && trs.Any())
                {
                    foreach (HtmlNode tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Any())
                        {
                          
                        }
                    }
                }
            }
        }
        #endregion
    }
}
