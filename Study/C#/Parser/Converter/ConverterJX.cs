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
using System.IO;
using System.Xml.Serialization;
using System.Web;
using System.Configuration;
using iOubo.iSpider.Model.JiangXi;
using MongoDB.Bson;

namespace iOubo.iSpider.Infrastructure.Parser
{
    public class ConverterJX : IConverter
    {
        DataRequest _request;
        RequestInfo _requestInfo;
        RequestXml _requestXml;
        EnterpriseInfo _enterpriseInfo = new EnterpriseInfo();
        List<AbnormalInfo> _abnormals = new List<AbnormalInfo>();
        List<CheckupInfo> _checkups = new List<CheckupInfo>();
        bool isGT = false;
        List<string> reportsNeedToLoad = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings.Get("ReportsNeedToLoad"))
            ? new List<string>() : ConfigurationManager.AppSettings.Get("ReportsNeedToLoad").Split(',').ToList();
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
            List<ResponseInfo> responseList = null;
            _request.AddOrUpdateRequestParameter("currpage","1");
            responseList = GetResponseInfo(_request, _requestXml.GetRequestListByGroup("basic"));
            this.ParseResponse(responseList);

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

        private List<ResponseInfo> GetResponseInfo(DataRequest request, IEnumerable<XElement> elements)
        {
            List<ResponseInfo> responseList = new List<ResponseInfo>();
            foreach (XElement el in elements)
            {
                responseList.Add(request.RequestData(el, string.Empty));
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
                    case "basic":
                        this.LoadAndParseBasicInfo(responseInfo.Data);
                        break;
                    case "partner":
                        this.LoadAndParsePartner(responseInfo.Data, _enterpriseInfo);
                        break;
                    case "alter":
                        this.LoadAndParseAlter(responseInfo.Data, _enterpriseInfo);
                        break;
                    case "employee":
                        this.LoadAndParseEmployee(responseInfo.Data, _enterpriseInfo);
                        break;
                    case "branches":
                        this.LoadAndParseBranch(responseInfo.Data);
                        break;
                    case "guquanchuzhi":
                        this.LoadAndParsePledge(responseInfo.Data, _enterpriseInfo);
                        break;
                    case "financial_contribution":
                        this.LoadAndParseFinancialContribution(responseInfo.Data);
                        break;
                    case "mortgage":
                        this.LoadAndParseMortgage(responseInfo.Data);
                        break;
                    case "guquanbiangeng":
                        this.LoadAndParseStockChange(responseInfo.Data);
                        break;
                    case "xingzhengxuke":
                        this.LoadAndParseLicense(responseInfo.Data);
                        break;
                    case "xingzhengxuke_qy":
                        this.LoadAndParseLicense_qy(responseInfo.Data);
                        break;
                    case "zhishichanquan":
                        this.LoadAndParseKnowledge(responseInfo.Data);
                        break;
                    case "administrative_pinishment":
                        this.LoadAndParseAdministrativePunishment(responseInfo.Data);
                        break;
                    case "judicial_freeze":
                        this.LoadAndParseJudicailFreeze(responseInfo.Data);
                        break;
                    case "jingyin":
                        this.LoadAndParseJingyin(responseInfo.Data, _abnormals);
                        break;
                    case "check":
                        this.LoadAndParseCheck(responseInfo.Data, _checkups);
                        break;
                    case "report":
                        this.LoadAndParseReport(responseInfo.Data, _enterpriseInfo);
                        break;
                    //case "reportgt":
                    //    LoadAndParseReport(responseInfo.Data, _enterpriseInfo);
                    //    break;
                    default:
                        break;
                }
            }
        }

        #endregion

        #region 解析工商公示信息：基本信息
        /// <summary>
        /// 解析工商公示信息：基本信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseBasicInfo(string responseData)
        {
            int pages = 1;
            responseData = FormatJsonData(responseData,ref pages);
            var basicInfo = JsonConvert.DeserializeObject<JXBasicInfo>(responseData);
            Utility.ClearNullValue<JXBasicInfo>(basicInfo);
            _enterpriseInfo.reg_no = basicInfo.REGNO;
            _enterpriseInfo.credit_no = basicInfo.UNISCID;
            _enterpriseInfo.name = basicInfo.ENTNAME;
            _enterpriseInfo.econ_kind = basicInfo.ENTTYPE_CN;
            _enterpriseInfo.oper_name = basicInfo.NAME;
            if (!string.IsNullOrEmpty(basicInfo.DOM))
            {
                Address address = new Address();
                address.name = "注册地址";
                address.address = basicInfo.DOM;
                address.postcode = "";
                _enterpriseInfo.addresses.Add(address);
            }
            _enterpriseInfo.regist_capi = Convert.ToString(basicInfo.REGCAP) + "万" + basicInfo.REGCAPCUR_CN;
            _enterpriseInfo.start_date = basicInfo.ESTDATE;
            _enterpriseInfo.term_start = basicInfo.OPFROM;
            _enterpriseInfo.term_end = basicInfo.OPTO;
            _enterpriseInfo.scope = basicInfo.OPSCOPE;
            _enterpriseInfo.belong_org = basicInfo.REGORG_CN;
            _enterpriseInfo.check_date = basicInfo.APPRDATE;
            _enterpriseInfo.status = basicInfo.REGSTATE_CN;

            // _enterpriseInfo.end_date= basicInfo.REGSTATE_CN;
            // _enterpriseInfo. = tdList[i - 1].InnerText.Trim();

        }
        #endregion

        #region 解析分支机构
        /// <summary>
        /// 解析分支机构
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseBranch(string responseData)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;
            var anonymous = new[] { new { REGORG_CN = "", REGNO = "", BRNAME = "" } };
            var arr = JsonConvert.DeserializeAnonymousType(responseData, anonymous);
            if (arr != null)
            {
                foreach (var item in arr)
                {
                    Branch branch = new Branch();
                    branch.seq_no = _enterpriseInfo.branches.Count + 1;
                    branch.name = item.BRNAME;
                    branch.reg_no = item.REGNO;
                    branch.belong_org = item.REGORG_CN;
                    _enterpriseInfo.branches.Add(branch);
                }
            }
            
        } 
        #endregion

        #region 解析动产抵押
        /// <summary>
        /// 动产抵押
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseMortgage(string responseData)
        {
            var request = this.CreateRequest();
            var ran = new Random();
            int pages = 1;
            responseData = FormatJsonData(responseData, ref pages);
            List<JXMortgages> mortgages = JsonConvert.DeserializeObject<JXMortgages[]>(responseData).ToList<JXMortgages>();
            HandleMultiPages<JXMortgages>(pages, mortgages, "mortgage");
            for (int j = 0; j < mortgages.Count(); j++)
            {
                Utility.ClearNullValue<JXMortgages>(mortgages[j]);
                MortgageInfo item = new MortgageInfo();
                item.seq_no = _enterpriseInfo.mortgages.Count + 1;
                item.number = mortgages[j].MORREGCNO;
                item.date = mortgages[j].REGIDATE;
                item.department = mortgages[j].REGORG_CN;
                item.amount = string.Format("{0}万元({1})", mortgages[j].PRICLASECAM, mortgages[j].REGCAPCUR_CN);
                item.status = mortgages[j].TYPE == "1" ? "有效" : "无效";
                item.public_date = mortgages[j].PUBLICDATE;
                request.AddOrUpdateRequestParameter("MORREG_ID", mortgages[j].MORREG_ID);
                request.AddOrUpdateRequestParameter("randommath",ran.NextDouble().ToString());
                var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("mortgage_detail"));
                if (responseList != null && responseList.Any() && !string.IsNullOrWhiteSpace(responseList.First().Data))
                {
                    this.LoadAndParseMortgageDetail(responseList.First().Data,item);
                }
                _enterpriseInfo.mortgages.Add(item);
            }
        }

        #endregion

        #region 解析动产抵押详情
        /// <summary>
        /// 解析动产抵押详情
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="mortgage"></param>
        void LoadAndParseMortgageDetail(string responseData,MortgageInfo mortgage)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var divs = rootNode.SelectNodes("//div[@class='sea_28']");
            if (divs != null && divs.Any())
            {
                foreach (var div in divs)
                {
                    var table = div.SelectSingleNode("./following-sibling::table[1]");
                    if (table != null)
                    {
                        var innerRows = table.SelectNodes("./tr");
                        if (div.InnerText.Contains("抵押权人概况信息"))
                        {
                            foreach (var row in innerRows)
                            {
                                var cells = row.SelectNodes("./td");
                                if (cells == null || cells.Count < 4) continue;
                                Mortgagee mortgagee = new Mortgagee();
                                mortgagee.seq_no = mortgage.mortgagees.Count + 1;
                                mortgagee.name = cells[1].InnerText;
                                mortgagee.identify_type = cells[2].InnerText;
                                mortgagee.identify_no = cells[3].InnerText;
                                mortgage.mortgagees.Add(mortgagee);
                            }
                        }
                        else if (div.InnerText.Contains("被担保债权概况信息"))
                        {
                            foreach (HtmlNode rowNode2 in innerRows)
                            {
                                HtmlNodeCollection thList2 = rowNode2.SelectNodes("./th");
                                HtmlNodeCollection tdList2 = rowNode2.SelectNodes("./td");

                                if (thList2 != null && tdList2 != null && thList2.Count == tdList2.Count)
                                {
                                    for (int i = 0; i < thList2.Count; i++)
                                    {
                                        switch (thList2[i].InnerText.Trim())
                                        {
                                            case "种类":
                                                mortgage.debit_type = tdList2[i].InnerText.Trim();
                                                break;
                                            case "数额":
                                                mortgage.debit_amount = tdList2[i].InnerText.Trim();
                                                break;
                                            case "担保的范围":
                                                mortgage.debit_scope = tdList2[i].InnerText.Trim();
                                                break;
                                            case "债务人履行债务的期限":
                                                mortgage.debit_period = tdList2[i].InnerText.Trim();
                                                break;
                                            case "备注":
                                                mortgage.debit_remarks = tdList2[i].InnerText.Trim();
                                                break;
                                        }
                                    }
                                }
                            }
                        }
                        else if (div.InnerText.Contains("抵押物概况信息"))
                        {
                            foreach (var row in innerRows)
                            {
                                var cells = row.SelectNodes("./td");
                                if (cells == null || cells.Count < 5) continue;
                                Guarantee guarantee = new Guarantee();
                                guarantee.seq_no = mortgage.guarantees.Count + 1;
                                guarantee.name = cells[1].InnerText;
                                guarantee.belong_to = cells[2].InnerText;
                                guarantee.desc = cells[3].InnerText;
                                guarantee.remarks = cells[4].InnerText;
                                mortgage.guarantees.Add(guarantee);
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region 股权出质
        /// <summary>
        ///股权出质
        /// </summary>
        /// <param name="responseInfoList"></param>
        /// <param name="mortgageInfo"></param>
        /// 
        private void LoadAndParsePledge(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            int pages = 1;
            List<EquityQuality> list = new List<EquityQuality>();
            responseData = FormatJsonData(responseData,ref pages);
            List<JXEquities> equities = JsonConvert.DeserializeObject<JXEquities[]>(responseData).ToList < JXEquities>();
            HandleMultiPages<JXEquities>(pages, equities, "guquanchuzhi");
            for (int j = 0; j < equities.Count(); j++)
            {
                Utility.ClearNullValue<JXEquities>(equities[j]);
                EquityQuality item = new EquityQuality();
                item.seq_no = list.Count + 1;
                item.number = equities[j].EQUITYNO;
                item.pledgor = equities[j].PLEDGOR;
                item.pledgor_identify_no = equities[j].PLEDBLICNO;
                item.pledgor_amount = Convert.ToString(equities[j].IMPAM);
                item.pawnee = equities[j].IMPORG;
                item.pawnee_identify_no = equities[j].IMPORGBLICNO;
                item.date = equities[j].EQUPLEDATE;
                item.status = equities[j].TYPE=="1"?"有效":"无效";
                item.public_date = equities[j].PUBLICDATE;
                list.Add(item);
            }
            _enterpriseInfo.equity_qualities = list;
        }
        #endregion

        #region 股权变更
        /// <summary>
        /// 股权变更
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseStockChange(string responseData)
        {
            int pages = 1;
            List<StockChangeItem> scLst = new List<StockChangeItem>();
            responseData = FormatJsonData(responseData,ref pages);
            List<JXStockChanges> changes = JsonConvert.DeserializeObject<JXStockChanges[]>(responseData).ToList<JXStockChanges>();
            HandleMultiPages<JXStockChanges>(pages, changes, "guquanbiangeng");
            for (int j = 0; j < changes.Count(); j++)
            {
                Utility.ClearNullValue<JXStockChanges>(changes[j]);
                StockChangeItem item = new StockChangeItem();
                item.seq_no = scLst.Count + 1;
                item.name = changes[j].INV;
                item.before_percent = Convert.ToString(changes[j].TRANSAMPRBF);
                item.after_percent = Convert.ToString(changes[j].TRANSAMPRAF);
                item.change_date = changes[j].ALIDATE;
                item.public_date = changes[j].PUBLICDATE;
                scLst.Add(item);
            }
            _enterpriseInfo.stock_changes = scLst;

        }
        #endregion

        #region 行政许可
        /// <summary>
        /// 行政许可
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseLicense(string responseData)
        {
            int pages = 1;
            List<LicenseInfo> scLst = new List<LicenseInfo>();
            responseData = FormatJsonData(responseData,ref pages);
            List<JXLicenses> licenses = JsonConvert.DeserializeObject<JXLicenses[]>(responseData).ToList < JXLicenses>();
            HandleMultiPages<JXLicenses>(pages, licenses, "xingzhengxuke");
            for (int j = 0; j < licenses.Count(); j++)
            {
                Utility.ClearNullValue<JXLicenses>(licenses[j]);
                LicenseInfo item = new LicenseInfo();
                item.seq_no = scLst.Count + 1;
                item.number = licenses[j].LICNO;
                item.name = licenses[j].LICNAME_CN;
                item.start_date = licenses[j].VALFROM;
                item.end_date = licenses[j].VALTO;
                item.department = licenses[j].LICANTH;
                item.content = licenses[j].LICITEM; 
                item.status = licenses[j].TYPE == "1"?"有效":"无效";
                scLst.Add(item);
            }
            _enterpriseInfo.licenses = scLst;

        }
        #endregion

        #region 行政许可
        /// <summary>
        /// 行政许可
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseLicense_qy(string responseData)
        {
            int pages = 1;
            List<LicenseInfo> scLst = new List<LicenseInfo>();
            responseData = FormatJsonData(responseData, ref pages);
            List<JXLicenses> licenses = JsonConvert.DeserializeObject<JXLicenses[]>(responseData).ToList<JXLicenses>();
            HandleMultiPages<JXLicenses>(pages, licenses, "xingzhengxuke_qy");
            for (int j = 0; j < licenses.Count(); j++)
            {
                Utility.ClearNullValue<JXLicenses>(licenses[j]);
                LicenseInfo item = new LicenseInfo();
                item.seq_no = scLst.Count + 1;
                item.number = licenses[j].LICNO;
                item.name = licenses[j].LICNAME_CN;
                item.start_date = licenses[j].VALFROM;
                item.end_date = licenses[j].VALTO;
                item.department = licenses[j].LICANTH;
                item.content = licenses[j].LICITEM;
                item.status = licenses[j].TYPE == "1" ? "有效" : "无效";
                scLst.Add(item);
            }
            _enterpriseInfo.licenses = scLst;

        }
        #endregion

        #region 行政处罚
        /// <summary>
        /// 行政处罚
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseAdministrativePunishment(string responseData)
        {
            int pages = 1;
            responseData = this.FormatJsonData(responseData, ref pages);
            List<JXAdministrativePunishment> aps = JsonConvert.DeserializeObject<JXAdministrativePunishment[]>(responseData).ToList<JXAdministrativePunishment>();
            HandleMultiPages<JXAdministrativePunishment>(pages, aps, "administrative_pinishment");
            for (int j = 0; j < aps.Count(); j++)
            {
                Utility.ClearNullValue<JXAdministrativePunishment>(aps[j]);
                AdministrativePunishment ap = new AdministrativePunishment();
                ap.seq_no = _enterpriseInfo.administrative_punishments.Count + 1;
                ap.number = aps[j].PENDECNO;
                ap.illegal_type = aps[j].ILLEGACTTYPE;
                ap.department = aps[j].PENAUTH_CN;
                ap.content = aps[j].PENCONTENT;
                ap.date = aps[j].PENDECISSDATE;
                ap.remark = string.Empty;
                ap.name = _enterpriseInfo.name;
                ap.oper_name = _enterpriseInfo.oper_name;
                ap.public_date = aps[j].PUBLICDATE;
                ap.reg_no = string.IsNullOrWhiteSpace(_enterpriseInfo.reg_no) ? _enterpriseInfo.credit_no : _enterpriseInfo.reg_no;
                if (aps[j].CASEID == "1")
                {
                    //this.LoadAndParseAdministrativePunishmentDetail(aps[j].CASEID, ap);
                }
                
                _enterpriseInfo.administrative_punishments.Add(ap);
            }
           
        }

        #endregion

        #region 解析行政处罚详情
        void LoadAndParseAdministrativePunishmentDetail(string caseid,AdministrativePunishment ap)
        {
            Random ran = new Random();
            var request = this.CreateRequest();
            request.AddOrUpdateRequestParameter("caseid", caseid);
            var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("administrative_pinishment_detail"));
            if (responseList != null && responseList.Any())
            { 
                
            }
        }
        #endregion

        #region 解析股权冻结
        void LoadAndParseJudicailFreeze(string responseData)
        {
            int pages = 1;
            responseData = this.FormatJsonData(responseData, ref pages);
            List<JXJudicialFreeze> aps = JsonConvert.DeserializeObject<JXJudicialFreeze[]>(responseData).ToList<JXJudicialFreeze>();
            HandleMultiPages<JXJudicialFreeze>(pages, aps, "judicial_freeze");
            for (int j = 0; j < aps.Count(); j++)
            {
                Utility.ClearNullValue<JXJudicialFreeze>(aps[j]);
                JudicialFreeze jf = new JudicialFreeze();
                jf.seq_no = _enterpriseInfo.judicial_freezes.Count + 1;
                jf.be_executed_person = aps[j].INV;
                jf.amount = string.Format("{0}{1}{2}", aps[j].FROAM, aps[j].FORAMME, aps[j].REGCAPCUR_CN);
                jf.executive_court = aps[j].FROAUTH;
                jf.number = aps[j].EXECUTENO;
                jf.status = !string.IsNullOrWhiteSpace(aps[j].FROID) ? string.Format("股权冻结|{0}", aps[j].FROZSTATE_CN) : aps[j].FROZSTATE_CN;
                this.LoadAndParseJudicialFreezeDetail(aps[j].FROID, aps[j].MODIFYID, jf);
                jf.type = string.IsNullOrEmpty(jf.type) ? "股权冻结" : jf.type;
                _enterpriseInfo.judicial_freezes.Add(jf);
            }
        }
        #endregion

        #region 解析股权冻结详情信息
        void LoadAndParseJudicialFreezeDetail(string forid,string modifyid,JudicialFreeze jf)
        {
            var request = this.CreateRequest();
            if (!string.IsNullOrWhiteSpace(forid))
            {
                request.AddOrUpdateRequestParameter("FROID",Utility.EncodeBase64(forid));
                var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("judicial_freeze_detail_FROID"));
                if (responseList != null && responseList.Any())
                {
                    this.LoadAndParseJudicialFreezeDetailContent(responseList.First().Data,jf);
                }
            }
            else if (!string.IsNullOrWhiteSpace(modifyid))
            {
                request.AddOrUpdateRequestParameter("MODIFYID",Utility.EncodeBase64(modifyid));
                var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("judicial_freeze_detail_MODIFYID"));
                if (responseList != null && responseList.Any())
                {
                    this.LoadAndParseJudicialFreezeDetailContent(responseList.First().Data, jf);
                }
            }
        }
        #endregion

        #region 解析股权冻结详情内容
        void LoadAndParseJudicialFreezeDetailContent(string responseData,JudicialFreeze jf)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var divs = rootNode.SelectNodes("//div[@class='sea_28']");
            if (divs != null && divs.Any())
            {
                foreach (var div in divs)
                {
                    var table = div.SelectSingleNode("./following-sibling::table[1]");
                    if (table != null)
                    {
                        var trList = table.SelectNodes("./tr");
                        if (div.InnerText.Contains("股权冻结信息"))
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
                                jf.detail = freeze;
                            }
                        }
                        else if (div.InnerText.Contains("股权解冻信息"))
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
                                jf.un_freeze_detail = unfreeze;
                                jf.un_freeze_details.Add(unfreeze);
                            }
                        }
                        else if (div.InnerText.Contains("股权冻结失效信息"))
                        {
                            if (trList != null && trList.Count > 1)
                            {
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
                                                case "失效原因":
                                                    jf.lose_efficacy.reason = tdList[j].InnerText.Trim();
                                                    break;
                                                case "失效时间":
                                                    jf.lose_efficacy.date = tdList[j].InnerText.Trim();
                                                    break;
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        else if (div.InnerText.Contains("股东变更信息"))
                        {
                            jf.type = "股权变更";
                            JudicialFreezePartnerChangeDetail freeze = new JudicialFreezePartnerChangeDetail();
                            if (trList != null && trList.Count > 1)
                            {
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
                            }
                            jf.pc_freeze_detail = freeze;
                        }
                        
                    }
                }
            }
        }
        #endregion

        #region 股东出资信息
        /// <summary>
        /// 股东出资信息
        /// </summary>
        /// <param name="table"></param>
        /// <param name="_FinancialList"></param>
        private void LoadAndParseFinancialContribution(string responseData)
        {
            var request = this.CreateRequest();
            var ran = new Random();
            int pages = 1;
            responseData = this.FormatJsonData(responseData, ref pages);

            List<JXFinanceContribution> fcs = JsonConvert.DeserializeObject<JXFinanceContribution[]>(responseData).ToList<JXFinanceContribution>();
            HandleMultiPages<JXFinanceContribution>(pages, fcs, "financial_contribution");
            for (int j = 0; j < fcs.Count(); j++)
            {
                Utility.ClearNullValue<JXFinanceContribution>(fcs[j]);
                FinancialContribution item = new FinancialContribution();
                item.seq_no = _enterpriseInfo.financial_contributions.Count + 1;
                item.investor_name = fcs[j].INV;
                item.total_should_capi = fcs[j].RJSSUM.ToString();
                item.total_real_capi = fcs[j].SJSUM;
                if (!string.IsNullOrWhiteSpace(fcs[j].RJFINFO))
                {
                    var arr = JsonConvert.DeserializeObject<JXRjfinfo[]>(fcs[j].RJFINFO).ToList<JXRjfinfo>();
                    foreach (var sj_item in arr)
                    {
                        FinancialContribution.ShouldCapiItem sj = new FinancialContribution.ShouldCapiItem();
                        sj.should_invest_type = sj_item.CONFORM_CN;
                        sj.should_capi = sj_item.SUBCONAM.ToString();
                        sj.should_invest_date = sj_item.CONDATE;
                        sj.public_date = sj_item.PUBLICDATE;
                        if (!string.IsNullOrWhiteSpace(sj.should_capi))
                        {
                            item.should_capi_items.Add(sj);
                        }
                        
                    }
                }
                if (!string.IsNullOrWhiteSpace(fcs[j].SJFINFO))
                {
                    var arr = JsonConvert.DeserializeObject<JXSjfinfo[]>(fcs[j].SJFINFO).ToList<JXSjfinfo>();
                    foreach (var rj_item in arr)
                    {
                        FinancialContribution.RealCapiItem rj = new FinancialContribution.RealCapiItem();
                        rj.real_invest_type = rj_item.CONFORM_CN;
                        rj.real_capi = rj_item.ACCONAM;
                        rj.real_invest_date = rj_item.CONDATE;
                        rj.public_date = rj_item.PUBLICDATE;
                        if (!string.IsNullOrWhiteSpace(rj.real_capi))
                        {
                            item.real_capi_items.Add(rj);
                        }
                        
                    }
                }

                _enterpriseInfo.financial_contributions.Add(item);
            }
            
        }
        #endregion

        #region 知识产权出质登记
        /// <summary>
        /// 知识产权出质登记
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseKnowledge(string responseData)
        {
            
            int pages = 1;
            responseData = this.FormatJsonData(responseData, ref pages);
            List<JXKnowledgeProperty> aps = JsonConvert.DeserializeObject<JXKnowledgeProperty[]>(responseData).ToList<JXKnowledgeProperty>();
            HandleMultiPages<JXKnowledgeProperty>(pages, aps, "zhishichanquan");
            for (int j = 0; j < aps.Count(); j++)
            {
                Utility.ClearNullValue<JXKnowledgeProperty>(aps[j]);
                KnowledgeProperty kp = new KnowledgeProperty();
                kp.seq_no = _enterpriseInfo.knowledge_properties.Count + 1;
                kp.number = string.IsNullOrWhiteSpace(aps[j].UNISCID) ? aps[j].REGNO : aps[j].UNISCID;
                kp.name = aps[j].TMNAME;
                if (aps[j].KINDS == "1")
                {
                    kp.type = "商标";
                }
                else if (aps[j].KINDS == "2")
                {
                    kp.type = "版权";
                }
                else
                {
                    kp.type = "专利";
                }
                kp.pledgor = aps[j].PLEDGOR;
                kp.pawnee = aps[j].IMPORG;
                kp.public_date = aps[j].PUBLICDATE;
                kp.period = string.Format("{0}~{1}", aps[j].PLEREGPERFROM, aps[j].PLEREGPERTO);
                kp.status = aps[j].TYPE == "1" ? "有效" : "无效";
                _enterpriseInfo.knowledge_properties.Add(kp);

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
            int pages = 1;
            List<Partner> partnerList = new List<Partner>();
            responseData = FormatJsonData(responseData,ref pages);
            List<JXPartnersInfo> partners = JsonConvert.DeserializeObject<JXPartnersInfo[]>(responseData).ToList<JXPartnersInfo>();
            HandleMultiPages<JXPartnersInfo>(pages, partners, "partner");
            for (int j=0;j<partners.Count();j++)
            {
                Utility.ClearNullValue<JXPartnersInfo>(partners[j]);
                Partner partner = new Partner();
                partner.stock_type = partners[j].INVTYPE_CN;
                partner.stock_name = partners[j].INV;
                partner.identify_type = partners[j].CERTYPE_CN;
                partner.identify_no = partners[j].CERNO;
                partner.seq_no = j+1;
                partner.stock_percent = "";
                partner.should_capi_items = new List<ShouldCapiItem>();
                partner.real_capi_items = new List<RealCapiItem>();
                LoadPartnerDetail(partner.should_capi_items, partner.real_capi_items, partners[j].INVID,partner);
                partnerList.Add(partner);
            }
            _enterpriseInfo.partners = partnerList;
        }
        #endregion

        #region 解析股东详情信息
        /// <summary>
        /// 解析股东详情信息
        /// </summary>
        /// <param name="shoulds"></param>
        /// <param name="reals"></param>
        /// <param name="invid"></param>
        private void LoadPartnerDetail(List<ShouldCapiItem> shoulds, List<RealCapiItem> reals,string invid,Partner partner)
        {
            var request = CreateRequest();
            request.AddOrUpdateRequestParameter("invid", invid);
            var data = request.RequestData(_requestXml.GetRequestItemByName("partnerdetail")).Data;
            if(!string.IsNullOrEmpty(data))
            {
                HtmlAgilityPack.HtmlDocument doc =new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(data);
                var table = doc.DocumentNode.SelectSingleNode("//table[@class='sea_41']");
                if(table!=null)
                {
                    var trs = table.SelectNodes("./tr");
                    if(trs!=null&&trs.Count==3)
                    {
                        partner.total_should_capi =trs[1].SelectNodes("./td")[0].InnerText;
                         partner.total_real_capi =trs[2].SelectNodes("./td")[0].InnerText;
                    }

                }
            }
        }
        #endregion

        #region 解析工商公示信息：变更信息
        /// <summary>
        /// 解析工商公示信息：变更信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseAlter(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            int pages = 1;
            List<ChangeRecord> changeRecordList = new List<ChangeRecord>();
            responseData = FormatJsonData(responseData,ref pages);
            List<JXChangeRecords> records = JsonConvert.DeserializeObject<JXChangeRecords[]>(responseData).ToList<JXChangeRecords>();

            HandleMultiPages<JXChangeRecords>(pages, records, "alter");
            
            for (int j = 0; j < records.Count(); j++)
            {
                Utility.ClearNullValue<JXChangeRecords>(records[j]);
                ChangeRecord changeRecord = new ChangeRecord();
                changeRecord.change_item = records[j].ALTITEM_CN;
                changeRecord.before_content = records[j].ALTBE;
                changeRecord.after_content = records[j].ALTAF;
                changeRecord.change_date = records[j].ALTDATE;
                changeRecord.seq_no = j+1;
                changeRecordList.Add(changeRecord);
            }
            _enterpriseInfo.changerecords = changeRecordList;

        }
        #endregion

        #region HandleMultiPages
        private void HandleMultiPages<JsonType>(int pages, List<JsonType> records,string methodName,string reportYear="0")
        {
            if (pages > 1)
            {
                for (int page = 2; page <= pages; page++)
                {
                    var request = CreateRequest();
                    request.AddOrUpdateRequestParameter("currpage", page.ToString());
                    if(reportYear!="0")
                    {
                        request.AddOrUpdateRequestParameter("year", reportYear);
                    }
                    int temppages = 0;
                    var data = FormatJsonData(request.RequestData(_requestXml.GetRequestItemByName(methodName)).Data, ref temppages);
                    var range = JsonConvert.DeserializeObject<JsonType[]>(data);
                    if(range.Count()>0)
                        records.AddRange(range);
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
            int pages = 1;
            List<Employee> employeeList = new List<Employee>();
            responseData = FormatJsonData(responseData,ref pages);
            List<JXEmployees> employees = JsonConvert.DeserializeObject<JXEmployees[]>(responseData).ToList < JXEmployees>();
            HandleMultiPages<JXEmployees>(pages, employees, "employee");
            for (int j = 0; j < employees.Count(); j++)
            {
                Utility.ClearNullValue<JXEmployees>(employees[j]);
                Employee employee2 = new Employee();
                employee2.seq_no = j+1;
                employee2.name = employees[j].NAME;
                employee2.job_title = employees[j].POSITION_CN;
                employee2.cer_no = "";
                employeeList.Add(employee2);
            }
            _enterpriseInfo.employees = employeeList;

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
            int pages = 1;
            responseData = FormatJsonData(responseData,ref pages);
            List<JXAbnormals> abnormals = JsonConvert.DeserializeObject<JXAbnormals[]>(responseData).ToList<JXAbnormals>();
            HandleMultiPages<JXAbnormals>(pages, abnormals, "jingyin");
            for (int j = 0; j < abnormals.Count(); j++)
            {
                Utility.ClearNullValue<JXAbnormals>(abnormals[j]);
                AbnormalInfo item = new AbnormalInfo();
                item.in_reason = abnormals[j].SPECAUSE_CN;
                item.in_date = abnormals[j].ABNTIME;
                item.out_reason = abnormals[j].REMEXCPRES_CN;
                item.out_date = abnormals[j].REMDATE;
                item.department = abnormals[j].DECORG_CN;
                _abnormals.Add(item);
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
            int pages = 1;
            responseData = FormatJsonData(responseData,ref pages);
            List<JXCheckups> checks = JsonConvert.DeserializeObject<JXCheckups[]>(responseData).ToList < JXCheckups>();
            HandleMultiPages<JXCheckups>(pages, checks, "check");
            for (int j = 0; j < checks.Count(); j++)
            {
                Utility.ClearNullValue<JXCheckups>(checks[j]);
                CheckupInfo item = new CheckupInfo();
                item.department = string.IsNullOrEmpty(checks[j].INSAUTH_CN) ? checks[j].INSAUTH : checks[j].INSAUTH_CN;
                item.type = TransferCheckupType(checks[j].INSTYPE);
                item.date = checks[j].INSDATE;
                item.result = checks[j].INSRES_CN;
                _checkups.Add(item);
            }
        }

        private string TransferCheckupType(string type)
        {
            switch(type)
            {
                case "1":
                    return  "即时信息定向";
                    break;
                case "2":
                    return  "即时信息不定向";
                    break;
                case "3":
                    return  "年报不定向";
                    break;
                case "4":
                    return  "年报定向";
                    break;
                case "5":
                    return  "专项";
                    break;
            }
            return string.Empty;
        }
        #endregion

        #region 解析年报
        /// <summary>
        /// 解析年报
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseReport(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            try
            {
                List<Report> reportList = new List<Report>();
                int pages = 1;
                responseData = FormatJsonData(responseData,ref pages);
                JXAnualReportList[] years = JsonConvert.DeserializeObject<JXAnualReportList[]>(responseData);
                foreach (var year in years)
                {
                    var request = CreateRequest();

                    request.AddOrUpdateRequestParameter("year", year.ANCHEYEAR);
                    Report report = new Report();
                    report.report_name = year.ANCHEYEAR + "年度报告";
                    report.report_year = year.ANCHEYEAR;

                    List<ResponseInfo> responseList = GetResponseInfo(request, _requestXml.GetRequestListByGroup("reportdetail"));
                    ParseReport(responseList, report);
                    reportList.Add(report);

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
                    case "reportbasic":
                        LoadAndParseReportBasic(responseInfo.Data, report);
                        break;
                    case "reportwebsite":
                        LoadAndParseReportWebsite(responseInfo.Data, report);
                        break;
                    case "reportpartner":
                        LoadAndParseReportPartner(responseInfo.Data, report);
                        break;
                    case "reportstockchange":
                        LoadAndParseReportStockChanges(responseInfo.Data, report);
                        break;
                    case "reportinvestment":
                        LoadAndParseReportInvest(responseInfo.Data, report);
                        break;
                    case "reportupdaterecord":
                        LoadAndParseReportUpdateRecord(responseInfo.Data, report);
                        break;
                    default:
                        break;
                }
            }
        }
        #endregion

        #region ParseGTReport
        private void ParseGTReport(List<ResponseInfo> responseInfoList, Report report)
        {
            
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
            responseData = responseData.Trim('[').Trim(']');
            var basicInfo = JsonConvert.DeserializeObject<JXReportBasicInfo>(responseData);
            Utility.ClearNullValue<JXReportBasicInfo>(basicInfo);
            int year = 0;
            report.report_date = string.IsNullOrEmpty(basicInfo.LASTUPDATETIME.Trim())
                ? int.TryParse(report.report_year, out year) ? year + 1 + "-06-30" : report.report_year + "-06-30" : basicInfo.LASTUPDATETIME;
            report.reg_no = basicInfo.REGNO;
            report.credit_no = basicInfo.UNISCID;
            report.name = basicInfo.ENTNAME;
            report.telephone = basicInfo.TEL;
            report.address = basicInfo.ADDR;
            report.zip_code = basicInfo.POSTALCODE;
            report.email = basicInfo.EMAIL;
            report.if_invest = basicInfo.ISCHANGE == "1" ? "有" : "无";
            report.if_website = basicInfo.ISWEB == "1" ? "有" : "无";
            report.status = basicInfo.BUSST_CN;
            report.collegues_num = basicInfo.EMPNUMDIS == "2" ? "该企业选择不公示" : basicInfo.EMPNUM;
            report.if_equity = basicInfo.ISLETTER == "1" ? "有" : "无";
            report.oper_name = _enterpriseInfo.oper_name;
            report.total_equity = basicInfo.ASSGRODIS == "1" ? basicInfo.ASSGRO + "万元" : "该企业选择不公示";
            report.debit_amount = basicInfo.LIAGRODIS == "1" ? basicInfo.LIAGRO + "万元" : "该企业选择不公示";
            report.sale_income = basicInfo.VENDINCDIS == "1" ? basicInfo.VENDINC + "万元" : "该企业选择不公示";
            report.serv_fare_income = basicInfo.MAIBUSINCDIS == "1" ? basicInfo.MAIBUSINC + "万元" : "该企业选择不公示";
            report.profit_total = basicInfo.PROGRODIS == "1" ? basicInfo.PROGRO + "万元" : "该企业选择不公示";
            report.net_amount = basicInfo.NETINCDIS == "1" ? basicInfo.NETINC + "万元" : "该企业选择不公示";
            report.tax_total = basicInfo.RATGRODIS == "1" ? basicInfo.RATGRO + "万元" : "该企业选择不公示";
            report.profit_reta = basicInfo.TOTEQUDIS == "1" ? basicInfo.TOTEQU + "万元" : "该企业选择不公示";

            report.social_security.yanglaobx_num = string.IsNullOrWhiteSpace(basicInfo.SOCIALINSURANCETPTENO110) ? string.Empty : basicInfo.SOCIALINSURANCETPTENO110 + "人";
            report.social_security.shiyebx_num = string.IsNullOrWhiteSpace(basicInfo.SOCIALINSURANCETPTENO210) ? string.Empty : basicInfo.SOCIALINSURANCETPTENO210 + "人";
            report.social_security.yiliaobx_num = string.IsNullOrWhiteSpace(basicInfo.SOCIALINSURANCETPTENO310) ? string.Empty : basicInfo.SOCIALINSURANCETPTENO310 + "人";
            report.social_security.gongshangbx_num = string.IsNullOrWhiteSpace(basicInfo.SOCIALINSURANCETPTENO410) ? string.Empty : basicInfo.SOCIALINSURANCETPTENO410 + "人";
            report.social_security.shengyubx_num = string.IsNullOrWhiteSpace(basicInfo.SOCIALINSURANCETPTENO510) ? string.Empty : basicInfo.SOCIALINSURANCETPTENO510 + "人";
            report.social_security.dw_yanglaobx_js = string.IsNullOrWhiteSpace(basicInfo.TOTALWAGES_SO110) ? string.Empty : basicInfo.TOTALWAGES_SO110 + "万元";
            report.social_security.dw_shiyebx_js = string.IsNullOrWhiteSpace(basicInfo.TOTALWAGES_SO210) ? string.Empty : basicInfo.TOTALWAGES_SO210 + "万元";
            report.social_security.dw_yiliaobx_js = string.IsNullOrWhiteSpace(basicInfo.TOTALWAGES_SO310) ? string.Empty : basicInfo.TOTALWAGES_SO310 + "万元";
            report.social_security.dw_shengyubx_js = string.IsNullOrWhiteSpace(basicInfo.TOTALWAGES_SO510) ? string.Empty : basicInfo.TOTALWAGES_SO510 + "万元";
            report.social_security.bq_yanglaobx_je = string.IsNullOrWhiteSpace(basicInfo.TOTALPAYMENT_SO110) ? string.Empty : basicInfo.TOTALPAYMENT_SO110 + "万元";
            report.social_security.bq_shiyebx_je = string.IsNullOrWhiteSpace(basicInfo.TOTALPAYMENT_SO210) ? string.Empty : basicInfo.TOTALPAYMENT_SO210 + "万元";
            report.social_security.bq_yiliaobx_je = string.IsNullOrWhiteSpace(basicInfo.TOTALPAYMENT_SO310) ? string.Empty : basicInfo.TOTALPAYMENT_SO310 + "万元";
            report.social_security.bq_gongshangbx_je = string.IsNullOrWhiteSpace(basicInfo.TOTALPAYMENT_SO410) ? string.Empty : basicInfo.TOTALPAYMENT_SO410 + "万元";
            report.social_security.bq_shengyubx_je = string.IsNullOrWhiteSpace(basicInfo.TOTALPAYMENT_SO510) ? string.Empty : basicInfo.TOTALPAYMENT_SO510 + "万元";
            report.social_security.dw_yanglaobx_je = string.IsNullOrWhiteSpace(basicInfo.UNPAIDSOCIALINS_SO110) ? string.Empty : basicInfo.UNPAIDSOCIALINS_SO110 + "万元";
            report.social_security.dw_shiyebx_je = string.IsNullOrWhiteSpace(basicInfo.UNPAIDSOCIALINS_SO210) ? string.Empty : basicInfo.UNPAIDSOCIALINS_SO210 + "万元";
            report.social_security.dw_yiliaobx_je = string.IsNullOrWhiteSpace(basicInfo.UNPAIDSOCIALINS_SO310) ? string.Empty : basicInfo.UNPAIDSOCIALINS_SO310 + "万元";
            report.social_security.dw_gongshangbx_je = string.IsNullOrWhiteSpace(basicInfo.UNPAIDSOCIALINS_SO410) ? string.Empty : basicInfo.UNPAIDSOCIALINS_SO410 + "万元";
            report.social_security.dw_shengyubx_je = string.IsNullOrWhiteSpace(basicInfo.UNPAIDSOCIALINS_SO510) ? string.Empty : basicInfo.UNPAIDSOCIALINS_SO510 + "万元";
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
            int pages = 1;
            responseData = FormatJsonData(responseData, ref pages);
            List<WebsiteItem> websiteList = new List<WebsiteItem>();
            var webs = JsonConvert.DeserializeObject<JXReportWebsites[]>(responseData).ToList<JXReportWebsites>();
            HandleMultiPages<JXReportWebsites>(pages, webs, "reportwebsite",report.report_year);
            foreach(var web in webs)
            {
                Utility.ClearNullValue<JXReportWebsites>(web);
                WebsiteItem item = new WebsiteItem();
                item.seq_no = websiteList.Count()+1;
                item.web_type = web.WEBTYPE == "1" ? "网站" : "网店";
                item.web_name = web.WEBSITNAME;
                item.web_url = web.DOMAIN;
                websiteList.Add(item);
            }
            report.websites = websiteList;
        }
        #endregion

        #region 年报股权变更
        /// <summary>
        /// 年报股权变更
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportStockChanges(string responseData, Report report)
        {
            int pages = 1;
            responseData = FormatJsonData(responseData,ref pages);
            List<StockChangeItem> stock_changes = new List<StockChangeItem>();
            var changes = JsonConvert.DeserializeObject<JXReportStockChanges[]>(responseData).ToList<JXReportStockChanges>();
            HandleMultiPages<JXReportStockChanges>(pages, changes, "reportstockchange", report.report_year);
            foreach (var change in changes)
            {
                Utility.ClearNullValue<JXReportStockChanges>(change);
                StockChangeItem item = new StockChangeItem();
                item.seq_no = stock_changes.Count + 1;
                item.name = change.INV;
                item.before_percent = change.TRANSAMPR;
                item.after_percent = change.TRANSAMAFT;
                item.change_date = change.ALTDATE;
                stock_changes.Add(item);
            }
            report.stock_changes = stock_changes;
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
            int pages = 1;
            responseData = FormatJsonData(responseData,ref  pages);
            List<Partner> partnerList = new List<Partner>();
            var partners = JsonConvert.DeserializeObject<JXReportPartners[]>(responseData).ToList<JXReportPartners>();
            HandleMultiPages<JXReportPartners>(pages, partners, "reportpartner", report.report_year);
            foreach (var partner in partners)
            {
                Utility.ClearNullValue<JXReportPartners>(partner);
                Partner item = new Partner();
                item.seq_no = partnerList.Count() + 1;
                item.stock_name = partner.INVNAME;
                item.stock_type = string.Empty;
                item.identify_no = "";
                item.identify_type = "";
                item.stock_percent = "";
                item.ex_id = "";
                item.real_capi_items = new List<RealCapiItem>();
                item.should_capi_items = new List<ShouldCapiItem>();
                ShouldCapiItem sItem = new ShouldCapiItem();
                sItem.shoud_capi = partner.LISUBCONAM;
                sItem.should_capi_date = partner.SUBCONDATE;
                sItem.invest_type = partner.SUBCONFORM_CN;
                item.should_capi_items.Add(sItem);
                RealCapiItem rItem = new RealCapiItem();
                rItem.real_capi = partner.LIACCONAM;
                rItem.real_capi_date = partner.ACCONDATE;
                rItem.invest_type = partner.ACCONFORM_CN;
                item.real_capi_items.Add(rItem);
                partnerList.Add(item);
            }
            report.partners = partnerList;
        }
        #endregion

        #region 年报对外投资信息
        /// <summary>
        /// 年报对外投资信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportInvest(string responseData, Report report)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;
            var anonymous = new[] { new { ENTNAME = "", UNISCID = "", OUTINVID = "" } };
            var arr = JsonConvert.DeserializeAnonymousType(responseData, anonymous);
            if (arr != null)
            {
                foreach (var item in arr)
                {
                    InvestItem invest = new InvestItem();
                    invest.seq_no = report.invest_items.Count + 1;
                    invest.invest_name = item.ENTNAME;
                    invest.invest_reg_no = item.UNISCID;
                    report.invest_items.Add(invest);
                }
            }
        }
        #endregion

        #region 年报修改信息
        /// <summary>
        /// 年报修改信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        void LoadAndParseReportUpdateRecord(string responseData, Report report)
        {
            int pages = 1;
            responseData = FormatJsonData(responseData, ref pages);
            var rds = JsonConvert.DeserializeObject<JXReportUpdateRecord[]>(responseData).ToList<JXReportUpdateRecord>();
            HandleMultiPages<JXReportUpdateRecord>(pages, rds, "reportupdaterecord", report.report_year);
            foreach (var rd in rds)
            {
                Utility.ClearNullValue<JXReportUpdateRecord>(rd);
                UpdateRecord item = new UpdateRecord();
                item.seq_no = report.update_records.Count + 1;
                item.update_item = rd.ALITEMS;
                item.before_update = rd.ALTBE;
                item.after_update = rd.ALTAF;
                item.update_date = rd.ALTDATE;
                report.update_records.Add(item);
            }
            
        }
        #endregion

        #region RemoveUnexceptedChar
        /// <summary>
        /// RemoveUnexceptedChar
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        private string RemoveUnexceptedChar(string str)
        {
            int index = str.IndexOf("-->");
            if (index > -1)
            {
                str = str.Substring(index + 3);
            }
            return str.Replace("\r\n", "").Replace("\t", "").Replace("&nbsp;", "").Trim();
        }
        #endregion

        #region FormatJsonData
        public string FormatJsonData(string data,ref int totalPage)
        {
            var total = Regex.Match(data, "\"totalPage\".*?\\,");
            if(total.Success)
            {
                var pageStr = total.Value.Replace("\"totalPage\":", "").Trim(',');
                int.TryParse(pageStr, out totalPage);
            }
            var match = Regex.Match(data,"\"data\":\\[.*?\\],\"page\"");
            if(match.Success)
            {
                return match.Value.Replace("\"data\":", "").Replace(",\"page\"","");
            }
            return data;
        }
        #endregion

        #region  CreateRequest
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
    }
}
