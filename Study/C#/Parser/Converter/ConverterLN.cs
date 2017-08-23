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
using System.Configuration;

namespace iOubo.iSpider.Infrastructure.Parser
{
    public class ConverterLN : IConverter 
    {
        DataRequest _request;
        RequestInfo _requestInfo;
        RequestXml _requestXml;
        EnterpriseInfo _enterpriseInfo = new EnterpriseInfo();
        List<AbnormalInfo> _abnormals = new List<AbnormalInfo>();
        List<CheckupInfo> _checkups = new List<CheckupInfo>();
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

            //解析基本信息：基本信息、股东信息、变更信息、主要人员信息、分支机构信息、经营异常信息、抽查检查信息
            List<ResponseInfo> responseList = _request.GetResponseInfo(_requestXml.GetRequestListByGroup("basic"));
            Parallel.ForEach(responseList, new ParallelOptions() { MaxDegreeOfParallelism = 1 }, responseInfo => ParseResponse(responseInfo));

            SummaryEntity summaryEntity = new SummaryEntity();
            summaryEntity.Enterprise = _enterpriseInfo;
            summaryEntity.Abnormals = _abnormals;
            summaryEntity.Checkups = _checkups;
            if (summaryEntity.Enterprise.partners_hidden.Any())
            {
                summaryEntity.Enterprise.partner_hidden_flag = 1;
            }
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

        #region 解析企业信息
        /// <summary>
        /// 解析企业信息
        /// </summary>
        /// <param name="responseInfoList"></param>
        private void ParseResponse(ResponseInfo responseInfo)
        {
            switch (responseInfo.Name)
            {
                case "basicInfo":
                    LoadAndParseBasicInfo(responseInfo.Data, _enterpriseInfo);
                    break;
                case "xingzhengchufa":
                    LoadAndParseAdministrativePunishments(responseInfo.Data, _enterpriseInfo);
                    break;
                case "partner":
                    LoadAndParsePartner(responseInfo.Data, _enterpriseInfo);
                    break;
                case "alter":
                    LoadAndParseAlter(responseInfo.Data, _enterpriseInfo);
                    break;
                case "employee":
                    LoadAndParseEmployee(responseInfo.Data, _enterpriseInfo);
                    break;
                case "branch":
                    LoadAndParseBranch(responseInfo.Data, _enterpriseInfo);
                    break;
                case "abnormal":
                    LoadAndParseAbnormal(responseInfo.Data, _enterpriseInfo);
                    break;
                case "check":
                    LoadAndParseCheck(responseInfo.Data, _enterpriseInfo);
                    break;
                case "guquanchuzhi":
                    LoadAndParseEquityQuality(responseInfo.Data, _enterpriseInfo);
                    break;
                case "gudongchuzi":
                    LoadAndParseFinancialContribution(responseInfo.Data);
                    break;
                case "gudongchuzibiangeng":
                    LoadAndParseUpdateRecords(responseInfo.Data, _enterpriseInfo);
                    break;
                case "report":
                    LoadAndParseReport(responseInfo.Data, _enterpriseInfo);
                    break;
                case "license":
                    LoadAndParseLicenseInfo(responseInfo.Data);
                    break;
                case "license_qy":
                    LoadAndParseLicenseInfo_QY(responseInfo.Data);
                    break;
                case "dongchandiya":
                    LoadAndParseMortgageInfoItems(responseInfo.Data, _enterpriseInfo);
                    break;
                case "dongjie":
                    LoadAndParseFreeze(responseInfo.Data, _enterpriseInfo);
                    break;
                case "guquanbiangeng":
                    LoadAndParseStockChanges(responseInfo.Data, _enterpriseInfo);
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
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseBasicInfo(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            HtmlNodeCollection dlList = rootNode.SelectNodes("//dl");
            foreach (var dl in dlList)
            {
                if (dl.SelectSingleNode("./dt") == null || dl.SelectSingleNode("./dd") == null) continue;
                var title = dl.SelectSingleNode("./dt").InnerText;
                var content = dl.SelectSingleNode("./dd").InnerText;
                switch (title.Trim().Replace("：",""))
                {
                    case "注册号":
                        _enterpriseInfo.reg_no = content.Trim().Replace("&nbsp;", "");
                        break;
                    case "统一社会信用代码":
                        _enterpriseInfo.credit_no = content.Trim().Replace("&nbsp;", "");
                        break;
                    case "注册号/统一社会信用代码":
                    case "统一社会信用代码/注册号":
                        if (content.Trim().Replace("&nbsp;", "").Length == 18)
                            _enterpriseInfo.credit_no = content.Trim().Replace("&nbsp;", "");
                        else
                            _enterpriseInfo.reg_no = content.Trim().Replace("&nbsp;", "");
                        break;
                    case "企业名称":
                    case "名称":
                        _enterpriseInfo.name = content.Trim().Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                        break;
                    case "类型":
                        _enterpriseInfo.econ_kind = content.Trim().Replace("null", "").Replace("NULL", "");
                        break;
                    case "法定代表人":
                    case "负责人":
                    case "股东":
                    case "经营者":
                    case "执行事务合伙人":
                    case "投资人":
                        _enterpriseInfo.oper_name = content.Trim().Replace("null", "").Replace("NULL", "");
                        break;
                    case "住所":
                    case "经营场所":
                    case "营业场所":
                    case "主要经营场所":
                        Address address = new Address();
                        address.name = "注册地址";
                        string add = content.Trim();
                        int index= add.IndexOf("\r\n");
                        if(index>0)
                        {
                            address.address = content.Trim().Substring(0, index);
                        }
                        else
                        {
                            address.address = content.Trim();
                        }
                        address.postcode = "";
                        _enterpriseInfo.addresses.Add(address);
                        break;
                    case "注册资金":
                    case "注册资本":
                    case "成员出资总额":
                        _enterpriseInfo.regist_capi = RemoveUnexpectedChar(content);
                        break;
                    case "成立日期":
                    case "登记日期":
                    case "注册日期":
                        _enterpriseInfo.start_date = content.Trim();
                        break;
                    case "营业期限自":
                    case "经营期限自":
                    case "合伙期限自":
                        _enterpriseInfo.term_start = content.Trim();
                        break;
                    case "营业期限至":
                    case "经营期限至":
                    case "合伙期限至":
                        _enterpriseInfo.term_end = content.Trim();
                        break;
                    case "经营范围":
                    case "业务范围":
                        _enterpriseInfo.scope = content.Trim().Replace("null", "").Replace("NULL", "");
                        break;
                    case "登记机关":
                        _enterpriseInfo.belong_org = content.Trim();
                        break;
                    case "核准日期":
                        _enterpriseInfo.check_date = content.Trim();
                        break;
                    case "登记状态":
                        _enterpriseInfo.status = dl.SelectSingleNode("./dd").FirstChild.InnerText.Replace("\r\n", "");
                        break;
                    case "吊销日期":
                    case "注销日期":
                        _enterpriseInfo.end_date = content.Trim();
                        break;
                    default:
                        break;
                }
            }
        }
        #endregion

        #region 行政处罚信息
        /// <summary>
        /// 行政处罚信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseAdministrativePunishments(string responseData, EnterpriseInfo _enterpriseInfo)
        {

            string arrayString = SubstringJsonData(responseData);
            if (string.IsNullOrEmpty(arrayString)) return;
            var list = JsonConvert.DeserializeObject<List<AdministrativePunishmentLN>>(arrayString);
            foreach (var item in list)
            {
                AdministrativePunishment ap = new AdministrativePunishment();
                ap.seq_no = _enterpriseInfo.administrative_punishments.Count + 1;
                ap.number = item.pendecno;
                ap.illegal_type = item.illegacttype;
                ap.date = item.pendecissdateStr;
                ap.content = item.pencontent;
                ap.department = item.penauthName;
                ap.name = _enterpriseInfo.name;
                ap.reg_no = string.IsNullOrWhiteSpace(_enterpriseInfo.reg_no) ? _enterpriseInfo.credit_no : _enterpriseInfo.reg_no;
                ap.oper_name = _enterpriseInfo.oper_name;
                ap.public_date = item.publicdateStr;
                _enterpriseInfo.administrative_punishments.Add(ap);
            }
        }
        #endregion

        #region 股东信息
        /// <summary>
        /// 股东信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParsePartner(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            string arrayString = SubstringJsonData(responseData);
            if (string.IsNullOrEmpty(arrayString)) return;
            //股东列表信息
            PartnerLN[] list = JsonConvert.DeserializeObject<PartnerLN[]>(arrayString);
            Dictionary<int, PartnerLN> dic = new Dictionary<int, PartnerLN>();
            for (int i = 0; i < list.Length; i++)
            {
                dic.Add(i + 1, list[i]);
            }
            if (list != null && list.Length > 0)
            {
                //var global_gdFlag = true;
                //if (responseData.Contains("var  global_gdFlag=\"false\";"))
                //{
                //    global_gdFlag = false;
                //}

                this.LoadAndParsePartner_Parallel(list.First());
                if (_enterpriseInfo.partners.Any())
                {
                    _enterpriseInfo.partners.Sort(new PartnerComparer());
                }
                if (_enterpriseInfo.partners_hidden.Any())
                {
                    _enterpriseInfo.partners_hidden.Sort(new PartnerComparer());
                }
            }

        }
        #endregion

        #region LoadAndParsePartner_Parallel
        private void LoadAndParsePartner_Parallel(PartnerLN item)
        {
            var request = CreateRequest();
            // 股东详情
            request.AddOrUpdateRequestParameter("invid", item.invid);
            List<ResponseInfo> reponseList1 = request.GetResponseInfo(_requestXml.GetRequestListByName("partnerDetail"));

            ShouldCapiItem shouldCapiItem = null;
            RealCapiItem realCapiItem = null;
            List<PartnerDetail> partnerDetailList = Newtonsoft.Json.JsonConvert.DeserializeObject<List<PartnerDetail>>(reponseList1[0].Data);
            if (partnerDetailList != null && partnerDetailList.Count > 0)
            {
                foreach (PartnerDetail partnerDetail in partnerDetailList)
                {
                    Partner partner = new Partner();
                    partner.identify_no = partnerDetail.tRegTzrxx.blicno;
                    partner.identify_type = partnerDetail.tRegTzrxx.blictypeName;
                    partner.stock_name = partnerDetail.tRegTzrxx.inv;
                    partner.stock_type = partnerDetail.tRegTzrxx.invtypeName;
                    partner.seq_no = _enterpriseInfo.partners.Count + 1;
                    partner.stock_percent = "";
                    partner.total_should_capi = string.IsNullOrEmpty(partnerDetail.tRegTzrxx.lisubconam) ? "" : partnerDetail.tRegTzrxx.lisubconam;
                    partner.total_real_capi = string.IsNullOrEmpty(partnerDetail.tRegTzrxx.liacconam) ? "" : partnerDetail.tRegTzrxx.liacconam;
                    if (partnerDetail.tRegTzrrjxxList.Count() > 0)
                    {
                        foreach (TRegTzrrjxxList rjxx in partnerDetail.tRegTzrrjxxList)
                        {
                            shouldCapiItem = new ShouldCapiItem();
                            shouldCapiItem.shoud_capi = string.IsNullOrEmpty(rjxx.subconam) ? "" : rjxx.subconam;
                            shouldCapiItem.invest_type = rjxx.conformName;
                            shouldCapiItem.should_capi_date = rjxx.condate;
                            partner.should_capi_items.Add(shouldCapiItem);
                        }
                    }
                    if (partnerDetail.tRegTzrsjxxList.Count() > 0)
                    {
                        foreach (TRegTzrsjxxList sjxx in partnerDetail.tRegTzrsjxxList)
                        {
                            realCapiItem = new RealCapiItem();
                            realCapiItem.real_capi = string.IsNullOrEmpty(sjxx.acconam) ? "" : sjxx.acconam;
                            realCapiItem.invest_type = sjxx.conformName;
                            realCapiItem.real_capi_date = sjxx.condate;
                            partner.real_capi_items.Add(realCapiItem);
                        }
                    }
                   
                    _enterpriseInfo.partners.Add(partner);
                }
            }
        }
        #endregion

        #region 变更信息
        /// <summary>
        /// 变更信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseAlter(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            List<ChangeRecord> changeRecordList = new List<ChangeRecord>();
            string arrayString = SubstringJsonData(responseData);
            if (string.IsNullOrEmpty(arrayString)) return;
            AlterLN[] list = JsonConvert.DeserializeObject<AlterLN[]>(arrayString);
            int i = 1;
            foreach (AlterLN item in list)
            {
                ChangeRecord changeRecord = new ChangeRecord();
                changeRecord.change_item = item.altitemName;
                changeRecord.before_content = item.altbe;
                changeRecord.after_content = item.altaf;
                changeRecord.change_date = item.altdate;
                changeRecord.seq_no = i++;

                changeRecordList.Add(changeRecord);
            }

            _enterpriseInfo.changerecords = changeRecordList;
        }
        #endregion

        #region 主要人员信息
        /// <summary>
        /// 主要人员信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseEmployee(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            List<Employee> employeeList = new List<Employee>();

            if (!responseData.Contains("zyry_nm_paging"))
            {
                responseData = SubstringJsonDataWithComma(responseData).TrimEnd(',');
                if (string.IsNullOrEmpty(responseData)) return;
                EmployeeLN[] list = JsonConvert.DeserializeObject<EmployeeLN[]>(responseData);
                int i = 1;
                foreach (EmployeeLN item in list)
                {
                    Employee employee1 = new Employee();
                    employee1.job_title = item.positionName;
                    employee1.name = item.name;
                    employee1.seq_no = i++;
                    employee1.sex = "";
                    employee1.cer_no = "";

                    employeeList.Add(employee1);
                }

                _enterpriseInfo.employees = employeeList;
            }
        }
        #endregion

        #region 分支机构信息
        /// <summary>
        /// 分支机构信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseBranch(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            List<Branch> branchList = new List<Branch>();
            string arrayString = SubstringJsonDataWithComma(responseData);
            if (string.IsNullOrEmpty(arrayString)) return;
            BranchLN[] list = JsonConvert.DeserializeObject<BranchLN[]>(arrayString);
            int i = 1;
            foreach (BranchLN item in list)
            {
                Branch branch = new Branch();
                branch.belong_org = "";
                branch.name = item.brname;
                branch.seq_no = i++;
                branch.oper_name = "";
                branch.reg_no = item.regno;

                branchList.Add(branch);
            }

            _enterpriseInfo.branches = branchList;
        }
        #endregion

        #region 经营异常信息

        /// <summary>
        /// 经营异常信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseAbnormal(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            List<AbnormalInfo> abnList = new List<AbnormalInfo>();
            string arrayString = SubstringJsonData(responseData);
            if (string.IsNullOrEmpty(arrayString)) return;
            AbnormalLN[] jsonList = JsonConvert.DeserializeObject<AbnormalLN[]>(arrayString);
            foreach (AbnormalLN item in jsonList)
            {
                AbnormalInfo dItem = new AbnormalInfo();
                dItem.name = _enterpriseInfo.name;
                dItem.reg_no = _enterpriseInfo.reg_no;
                dItem.province = _enterpriseInfo.province;
                dItem.in_reason = item.specauseName;
                dItem.in_date = item.abnDate;
                dItem.out_reason = item.remexcpresName;
                dItem.out_date = item.remDate;
                dItem.department = item.lrregorgName;

                abnList.Add(dItem);
            }

            _abnormals = abnList;
        }
        #endregion

        #region 抽查检查信息
        /// <summary>
        /// 抽查检查信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseCheck(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            List<CheckupInfo> list = new List<CheckupInfo>();
            string arrayString = SubstringJsonData(responseData);
            if (string.IsNullOrEmpty(arrayString)) return;
            CheckLN[] jsonList = JsonConvert.DeserializeObject<CheckLN[]>(arrayString);
            foreach (CheckLN item in jsonList)
            {
                CheckupInfo checkup = new CheckupInfo();
                checkup.name = _enterpriseInfo.name;
                checkup.reg_no = _enterpriseInfo.reg_no;
                checkup.province = _enterpriseInfo.province;
                checkup.department = item.insauthName;
                checkup.type = item.instypeName;
                checkup.date = item.insdateStr;
                checkup.result = item.insresName;

                list.Add(checkup);
            }

            _checkups = list;
        }
        #endregion

        #region 解析股权出质登记信息
        /// <summary>
        /// 解析股权出质登记信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseEquityQuality(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            List<EquityQuality> list = new List<EquityQuality>();
            string arrayString = SubstringJsonData(responseData);
            if (string.IsNullOrEmpty(arrayString)) return;
            StockLN[] jsonList = JsonConvert.DeserializeObject<StockLN[]>(arrayString);
            foreach (StockLN item in jsonList)
            {
                Utility.ClearNullValue<StockLN>(item);
                EquityQuality equityquality = new EquityQuality();
                equityquality.seq_no = list.Count + 1;
                equityquality.number = item.equityno;
                equityquality.pledgor = item.pledgor;
                equityquality.pledgor_identify_no = item.blicno;
                equityquality.pledgor_amount = item.impam.ToString() + item.pledamunitName;
                equityquality.pawnee = item.imporg;
                equityquality.pawnee_identify_no = item.impcerno;
                equityquality.date = item.regdateStr;
                equityquality.status = item.typename;
                equityquality.public_date = item.gstimeStr;
                list.Add(equityquality);
            }

            _enterpriseInfo.equity_qualities = list;
        }

        #endregion


        #region 解析股权变更信息
        /// <summary>
        /// 解析股权变更信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseStockChanges(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            List<StockChangeItem> list = new List<StockChangeItem>();
            string arrayString = SubstringJsonData(responseData);
            if (string.IsNullOrEmpty(arrayString)) return;
            StockChangesLN[] jsonList = JsonConvert.DeserializeObject<StockChangesLN[]>(arrayString);
            for (int index = 0; index < jsonList.Count(); index++)
            {
                StockChangesLN item = jsonList[index];
                Utility.ClearNullValue<StockChangesLN>(item);
                StockChangeItem change = new StockChangeItem();
                change.seq_no = index + 1;
                change.name = item.inv;
                change.before_percent = string.IsNullOrEmpty(item.transamprbe)?string.Empty:item.transamprbe+"%";
                change.after_percent = string.IsNullOrEmpty(item.transampraf) ? string.Empty : item.transampraf + "%";
                change.change_date = item.altdateStr;
                change.public_date = item.gstimeStr;
                list.Add(change);

            }
            _enterpriseInfo.stock_changes = list;
        }
        #endregion

        #region 解析股东及出资信息
        /// <summary>
        /// 解析股东及出资信息
        /// </summary>
        /// <param name="requestInfo"></param>
        private void LoadAndParseFinancialContribution(string responseData)
        {
            //股东及出资信息          
            List<FinancialContribution> list = new List<FinancialContribution>();
            responseData = responseData.Replace("{\"jsonArray\":","");
            responseData = responseData.Substring(0, responseData.LastIndexOf(','));
            if (string.IsNullOrEmpty(responseData)) return;
            GDCZLN[] gdcz = JsonConvert.DeserializeObject<GDCZLN[]>(responseData);
            if (gdcz != null && gdcz.Count() > 0)
            {
                for (int i = 0; i < gdcz.Count(); i++)
                {
                    GDCZLN item = gdcz[i];
                    Utility.ClearNullValue<GDCZLN>(item);
                    FinancialContribution financialcontribution = new FinancialContribution();
                    financialcontribution.seq_no = list.Count + 1;
                    financialcontribution.investor_name = item.tJsTzrxx.inv;
                    financialcontribution.total_real_capi = item.tJsTzrxx.liacconam;
                    financialcontribution.total_should_capi = item.tJsTzrxx.lisubconam;
                    List<FinancialContribution.ShouldCapiItem> should_capi_items = new List<FinancialContribution.ShouldCapiItem>();
                    List<FinancialContribution.RealCapiItem> real_capi_items = new List<FinancialContribution.RealCapiItem>();


                    if (item.tJsTzrrjxxList != null && item.tJsTzrrjxxList.Any())
                    {
                        var arr = item.tJsTzrrjxxList;
                        foreach (var subItem in arr)
                        {
                            Utility.ClearNullValue<TJsTzrrjxxList>(subItem);
                            FinancialContribution.ShouldCapiItem CapiItem = new FinancialContribution.ShouldCapiItem();
                            CapiItem.should_invest_type = subItem.subconformName;
                            CapiItem.should_capi = subItem.subconam;
                            CapiItem.should_invest_date = subItem.subcondateStr;
                            CapiItem.public_date = subItem.gstimeStr;
                            should_capi_items.Add(CapiItem);
                        }
                        financialcontribution.should_capi_items = should_capi_items;
                    }
                    if (item.tJsTzrsjxxList != null && item.tJsTzrsjxxList.Any())
                    {
                        var arr = item.tJsTzrsjxxList;
                        foreach (var acItem in arr)
                        {
                            Utility.ClearNullValue<TJsTzrsjxxList>(acItem);
                            FinancialContribution.RealCapiItem ReCapiItem = new FinancialContribution.RealCapiItem();
                            ReCapiItem.real_invest_type = acItem.acconformName;
                            ReCapiItem.real_capi = acItem.acconam;
                            ReCapiItem.real_invest_date = acItem.accondateStr;
                            ReCapiItem.public_date = acItem.gstimeStr;
                            real_capi_items.Add(ReCapiItem);
                        }
                        financialcontribution.real_capi_items = real_capi_items;
                    }

                    list.Add(financialcontribution);
                }
            }

            _enterpriseInfo.financial_contributions = list;
        }

        private void LoadAndParseUpdateRecords(string responseData, EnterpriseInfo _enterpriseInfo)
        {

            // 股东及出资信息变更记录
            //List<UpdateRecord> updatedRecords = new List<UpdateRecord>();
            //string arrayString = SubstringJsonData(responseData);
            //UpdatedRecords[] gdcz = JsonConvert.DeserializeObject<UpdatedRecords[]>(arrayString);
            //if (gdcz != null)
            //{
            //    foreach (var modify in gdcz)
            //    {
            //        Utility.ClearNullValue<UpdatedRecords>(modify);
            //        UpdateRecord updateRecord = new UpdateRecord();
            //        updateRecord.seq_no = updatedRecords.Count + 1;
            //        updateRecord.before_update = modify.altbe;
            //        updateRecord.update_date = modify.altdateStr;
            //        updateRecord.update_item = modify.alt;
            //        updateRecord.after_update = modify.altaf;
            //        updatedRecords.Add(updateRecord);
            //    }
            //}
            //_enterpriseInfo.update_records = updatedRecords;

        }
        #endregion

        #region 解析动产抵押登记信息
        /// <summary>
        /// 解析动产抵押登记信息
        /// </summary>
        /// <param name="cqInfo"></param>
        private void LoadAndParseMortgageInfoItems(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            List<MortgageInfo> list = new List<MortgageInfo>();
            string arrayString = SubstringJsonData(responseData);
            if (string.IsNullOrEmpty(arrayString)) return;
            MotageLN[] jsonList = JsonConvert.DeserializeObject<MotageLN[]>(arrayString);
            if (jsonList != null && jsonList.Count() > 0)
            {
                for (int i = 0; i < jsonList.Count(); i++)
                {
                    MotageLN item = jsonList[i];
                    Utility.ClearNullValue<MotageLN>(item);
                    MortgageInfo mortgageinfo = new MortgageInfo();
                    mortgageinfo.seq_no = list.Count + 1;
                    mortgageinfo.number = item.morregcno;
                    mortgageinfo.date = item.regidateStr;
                    mortgageinfo.amount = item.priclasecam == "" ? item.priclasecam : item.priclasecam;
                    mortgageinfo.status = item.typeName;
                    mortgageinfo.department = item.regorgName;
                    mortgageinfo.public_date = item.gstimeStr;
                    // 解析动产抵押登记详情
                    var request = CreateRequest();
                    request.AddOrUpdateRequestParameter("dcdydjid", item.dcdydjid);
                    request.AddOrUpdateRequestParameter("pripid", _enterpriseInfo.parameters["pripid"]);
                    var xml = _requestXml.GetRequestListByName("diya_detials");
                    List<ResponseInfo> reponseList = request.GetResponseInfo(xml);
                    if (reponseList.Count() > 0)
                    {
                        LoadAndParseMortgageDetail(mortgageinfo, reponseList[0].Data);
                    }
                    list.Add(mortgageinfo);
                }
            }
            _enterpriseInfo.mortgages = list;

        }
        /// <summary>
        /// 解析动产抵押登记详情
        /// </summary>
        /// <param name="mortgageinfo"></param>
        /// <param name="response"></param>
        private void LoadAndParseMortgageDetail(MortgageInfo mortgageinfo,string response)
        {
            var matches = Regex.Matches(response, @"\[(.*?)\]", RegexOptions.Singleline | RegexOptions.Multiline);
            if (matches.Count != 2) return;

            MortgagerLN[] mortgagers = JsonConvert.DeserializeObject<MortgagerLN[]>(matches[0].Value);
            List<Mortgagee> mortgagees = new List<Mortgagee>();// 抵押权人概况
            if (mortgagers != null && mortgagers.Count() > 0)
            {
                for (int j = 0; j < mortgagers.Count(); j++)
                {
                    MortgagerLN item = mortgagers[j];
                    Mortgagee mortgagee = new Mortgagee();
                    mortgagee.seq_no = mortgagees.Count + 1;
                    mortgagee.name = item.more;
                    mortgagee.identify_type = item.certypeName;
                    mortgagee.identify_no = item.cerno;
                    mortgagees.Add(mortgagee);
                }
            }
            mortgageinfo.mortgagees = mortgagees;

            PawnLN[] pawns = JsonConvert.DeserializeObject<PawnLN[]>(matches[1].Value);

            List<Guarantee> guarantees = new List<Guarantee>();// 抵押物概况
            if (pawns != null && pawns.Count() > 0)
            {
                for (int j = 0; j < pawns.Count(); j++)
                {
                    PawnLN item = pawns[j];
                    Guarantee guarantee = new Guarantee();
                    guarantee.seq_no = guarantees.Count + 1;
                    guarantee.name = item.guaname;
                    guarantee.belong_to = item.own;
                    guarantee.desc = item.guades;
                    guarantee.remarks = item.remark;
                    guarantees.Add(guarantee);
                }
            }
            mortgageinfo.guarantees = guarantees;


             HtmlDocument document = new HtmlDocument();
            document.LoadHtml(response);
            HtmlNode rootNode = document.DocumentNode;

            List<MortgageInfo> MortgageList = new List<MortgageInfo>();

            HtmlNodeCollection tables = rootNode.SelectNodes("//table[@class='main-table']");
            if (tables != null)
            {
                foreach (HtmlNode table in tables)
                {
                    var nodes = table.SelectNodes("./tr/th");
                    if (nodes == null || nodes.Count == 0) continue;
                    if (table.PreviousSibling.PreviousSibling.InnerText=="被担保债权概况")
                    {
                        mortgageinfo.debit_type = table.SelectNodes("./tr/td")[0].InnerText;
                        mortgageinfo.debit_amount = table.SelectNodes("./tr/td")[1].InnerText.Replace("\r\n","").Replace("\n","").Replace("\t","");
                        mortgageinfo.debit_scope = table.SelectNodes("./tr/td")[2].InnerText;
                        mortgageinfo.debit_period = table.SelectNodes("./tr/td")[3].InnerText.Replace("\r\n", "").Replace("\n", "").Replace("\t", "");
                        mortgageinfo.debit_remarks = table.SelectNodes("./tr/td")[4].InnerText.Replace("\r\n", "").Replace("\n", "").Replace("\t", "");
                    }
                }
            }
        }

        #endregion

        #region 解析股权冻结
        /// <summary>
        /// 解析股权冻结信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseFreeze(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            List<JudicialFreeze> list = new List<JudicialFreeze>();
            var matches = Regex.Matches(responseData,@"\[{(.*?)}\]", RegexOptions.Singleline | RegexOptions.Multiline);
            if (matches.Count != 1) return;
            GqdjLN[] jsonList = JsonConvert.DeserializeObject<GqdjLN[]>(matches[0].Value);
            for (int index=0; index < jsonList.Count();index++)
            {
                GqdjLN item = jsonList[index];
                Utility.ClearNullValue<GqdjLN>(item);
                JudicialFreeze freeze = new JudicialFreeze();
                freeze.seq_no = index + 1;
                freeze.be_executed_person = item.inv;
                freeze.amount = item.froam;
                freeze.executive_court = item.zxfy;
                freeze.number = item.djwh;
                freeze.status = item.djztName;
                var request = CreateRequest();
                request.AddOrUpdateRequestParameter("gqdjxxseq", item.gqdjxxseq);
                request.AddOrUpdateRequestParameter("invid", item.invid);
                request.AddOrUpdateRequestParameter("ztgqdjxxseq", item.ztgqdjxxseq);
                request.AddOrUpdateRequestParameter("djzt", item.djzt);
                request.AddOrUpdateRequestParameter("hasYcjymlxx", "false");
                var xml = _requestXml.GetRequestListByName("dongjiexiangqing");
                List<ResponseInfo> reponseList = request.GetResponseInfo(xml);
                if (reponseList.Count() > 0)
                {
                    LoadAndParseFreezeDetail(freeze, reponseList[0].Data);
                }
                list.Add(freeze);

            }
            _enterpriseInfo.judicial_freezes = list;
        }

        private void LoadAndParseFreezeDetail(JudicialFreeze freeze, string response)
        {
            var matches = Regex.Matches(response, @"\[{(.*?)}\]", RegexOptions.Singleline | RegexOptions.Multiline);
            if (matches.Count != 1) return;

            GqdjxqLN[] jasonList = JsonConvert.DeserializeObject<GqdjxqLN[]>(matches[0].Value);
            JudicialFreezeDetail details = new JudicialFreezeDetail();
            JudicialUnFreezeDetail unFreeze = new JudicialUnFreezeDetail();
            if (jasonList != null && jasonList.Count() > 0)
            {
                GqdjxqLN jason = jasonList[0];
                details.execute_court = jason.froauth;
                details.assist_item = jason.executeitemName;
                details.adjudicate_no = jason.frodocno;
                details.notice_no = jason.executeno;
                details.assist_name = jason.inv;
                details.freeze_amount = jason.froam;
                details.assist_ident_type = jason.certypeName;
                details.assist_ident_no = jason.cerno;
                details.freeze_start_date = jason.frofromString;
                details.freeze_end_date = jason.frotoString;
                details.freeze_year_month = jason.frozdeadlineString;
                details.public_date = jason.publicdateString;
                details.corp_name = jason.entname;
                freeze.detail = details;
                if (freeze.status == "解除冻结")
                {
                    unFreeze.execute_court = jason.thawauth;
                    unFreeze.assist_item = jason.thawexecuteitemName;
                    unFreeze.notice_no = jason.thawexecuteno;
                    unFreeze.adjudicate_no = jason.thawdocno;
                    unFreeze.assist_name = jason.inv;
                    unFreeze.freeze_amount = jason.froam;
                    unFreeze.assist_ident_type = jason.certypeName;
                    unFreeze.assist_ident_no = jason.cerno;
                    unFreeze.unfreeze_date = jason.thawdateString;
                    unFreeze.public_date = jason.thawpublicdateString;
                    freeze.un_freeze_detail = unFreeze;
                    freeze.un_freeze_details.Add(unFreeze);
                }
                freeze.type = "股权冻结";
            }

        }

        #endregion

        #region 解析年报

        /// <summary>
        /// 解析年报
        /// </summary>

        /// <param name="cqReqort"></param>
        /// <param name="requestInfo"></param>
        private void LoadAndParseReport(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            try
            {
                List<Report> reportList = new List<Report>();
                string arrayString = SubstringJsonData(responseData);
                if (string.IsNullOrEmpty(arrayString)) return;
                ReportLN[] jsonList = JsonConvert.DeserializeObject<ReportLN[]>(arrayString);
                //将年报由串行改为并行
                if (jsonList.Any())
                {
                    try
                    {
                        Parallel.ForEach(jsonList, item => LoadAndParseReport_Parallel(item, reportList));
                        reportList.Sort(new ReportComparer());
                    }
                    catch (AggregateException ex)
                    {
                        foreach (var inner in ex.InnerExceptions)
                        {
                            Console.WriteLine("LoadAndParseReport_ParallelError:" + inner.Message);
                        }
                        _enterpriseInfo.reports.Clear();
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

        #region 解析年报信息--并行
        /// <summary>
        /// 解析年报信息--并行
        /// </summary>
        /// <param name="item"></param>
        /// <param name="reportList"></param>
        void LoadAndParseReport_Parallel(ReportLN item, List<Report> reportList)
        {
            if (string.IsNullOrWhiteSpace(item.artid)) return;
            Report report = new Report();
            report.ex_id = item.artid;
            report.report_year = item.ancheyear;
            report.report_name = item.ancheyear + "年度报告";
            report.report_date = item.anchedateStr;
            if (!reportsNeedToLoad.Any() || reportsNeedToLoad.Contains(report.report_year))
            {
                var request = CreateRequest();
                // 详细年报 
                request.AddOrUpdateRequestParameter("artId", item.artid);
                List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("report"));
                if (responseList != null && responseList.Count > 0)
                {
                    LoadAndParseReportsDetail(responseList[0].Data, report);
                }
                reportList.Add(report);
            }
        }
        #endregion

        #region 解析行政许可信息
        /// <summary>
        /// 解析行政许可信息
        /// </summary>
        /// <param name="requestInfo"></param>
        private void LoadAndParseLicenseInfo(string responseData)
        {
            //行政许可信息
            List<LicenseInfo> list = new List<LicenseInfo>();
            var resutl = Regex.Match(responseData, @"\[{(.*?)}\]");
            XzxkLN[] jsonList = JsonConvert.DeserializeObject<XzxkLN[]>(resutl.Value);
            if (jsonList != null && jsonList.Count() > 0)
            {
                for (int i = 0; i < jsonList.Count(); i++)
                {
                    XzxkLN item = jsonList[i];
                    Utility.ClearNullValue<XzxkLN>(item);
                    LicenseInfo licenseinfo = new LicenseInfo();
                    licenseinfo.seq_no = _enterpriseInfo.licenses.Count + 1;
                    licenseinfo.number = item.licno;
                    licenseinfo.name = item.licnamevalue;
                    licenseinfo.start_date = item.valfromStr;
                    licenseinfo.end_date = item.valtoStr;
                    licenseinfo.department = item.licanth;
                    licenseinfo.content = item.licitem;
                    licenseinfo.status = item.typename;

                    _enterpriseInfo.licenses.Add(licenseinfo);
                }
            }
            
        }
        #endregion

        #region 解析行政许可--工商
        /// <summary>
        /// 解析行政许可--工商
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseLicenseInfo_QY(string responseData)
        {
            //行政许可信息
            List<LicenseInfo> list = new List<LicenseInfo>();
            var resutl = Regex.Match(responseData, @"\[{(.*?)}\]");
            XzxkLN[] jsonList = JsonConvert.DeserializeObject<XzxkLN[]>(resutl.Value);
            if (jsonList != null && jsonList.Count() > 0)
            {
                for (int i = 0; i < jsonList.Count(); i++)
                {
                    XzxkLN item = jsonList[i];
                    Utility.ClearNullValue<XzxkLN>(item);
                    LicenseInfo licenseinfo = new LicenseInfo();
                    licenseinfo.seq_no = _enterpriseInfo.licenses.Count + 1;
                    licenseinfo.number = item.licno;
                    licenseinfo.name = item.licnamevalue;
                    licenseinfo.start_date = item.valfromStr;
                    licenseinfo.end_date = item.valtoStr;
                    licenseinfo.department = item.licanth;
                    licenseinfo.content = item.licitem;
                    licenseinfo.status = item.typename;

                    _enterpriseInfo.licenses.Add(licenseinfo);
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
        private void LoadAndParseReportsDetail(string responseData, Report report)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            var basicNode = rootNode.SelectSingleNode("//div[@class='gsfrnbxq_jbxx']");

            HtmlNodeCollection dlList = rootNode.SelectNodes("//dl");
            foreach (var dl in dlList)
            {
                var title = dl.SelectSingleNode("./dt").InnerText;
                var content = dl.SelectSingleNode("./dd").InnerText;
                switch (title.Trim().Replace("：", ""))
                {
                    case "注册号":
                        report.reg_no = content.Trim().Replace("&nbsp;", "");
                        break;
                    case "统一社会信用代码":
                        report.credit_no = content.Trim().Replace("&nbsp;", "");
                        break;
                    case "注册号/统一社会信用代码":
                    case "统一社会信用代码/注册号":
                        if (content.Trim().Replace("&nbsp;", "").Length == 18)
                            report.credit_no = content.Trim().Replace("&nbsp;", "");
                        else
                            report.reg_no = content.Trim().Replace("&nbsp;", "");
                        break;
                    case "企业名称":
                        report.name = content.Trim().Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                        break;
                    case "企业联系电话":
                        report.telephone = content.Trim();
                        break;
                    case "企业通信地址":
                        report.address = content.Trim();
                        break;
                    case "邮政编码":
                        report.zip_code = content.Trim();
                        break;
                    case "电子邮箱":
                    case "企业电子邮箱":
                        report.email = content.Trim();
                        break;
                    case "企业是否有投资信息或购买其他公司股权":
                    case "企业是否有对外投资设立企业信息":
                    case "是否有投资信息或购买其他公司股权":
                        report.if_invest = content.Trim();
                        break;
                    case "是否有网站或网店":
                    case "是否有网站或网点":
                        report.if_website = content.Trim();
                        break;
                    case "企业经营状态":
                        report.status = content.Trim();
                        break;
                    case "从业人数":
                        report.collegues_num = content.Trim();
                        break;
                    case "有限责任公司本年度是否发生股东股权转让":
                        report.if_equity = content.Trim();
                        break;
                    case "是否有对外提供担保信息":
                        report.if_external_guarantee = content.Trim();
                        break;
                    default:
                        break;
                }
            }

            var table = rootNode.SelectSingleNode("//div[@id='zczcxx']/table[@class='main-table']");
            if (table != null)
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
                             var res = RemoveUnexpectedChar((tdList[i].InnerText));
                             switch (thList[i].InnerText.Trim())
                             {
                                 case "资产总额":
                                     report.total_equity = res;
                                     break;
                                 case "负债总额":
                                     report.debit_amount = res;
                                     break;
                                 case "销售总额":
                                 case "营业总收入":
                                     report.sale_income = res;
                                     break;
                                 case "其中：主营业务收入":
                                 case "营业总收入中主营业务收入":
                                     report.serv_fare_income = res;
                                     break;
                                 case "利润总额":
                                     report.profit_total = res;
                                     break;
                                 case "净利润":
                                     report.net_amount = res;
                                     break;
                                 case "纳税总额":
                                     report.tax_total = res;
                                     break;
                                 case "所有者权益合计":
                                     report.profit_reta = res;
                                     break;
                                 default:
                                     break;
                             }
                         }
                     }
                 }
            }
            table = rootNode.SelectSingleNode("//div[@id='sbxx']/following-sibling::table[1]");
            if (table != null)
            {
                var trs = table.SelectNodes("./tr");
                if (trs != null && trs.Count == 6)
                {
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null)
                        {
                            if (tds.First().InnerText.Contains("城镇职工基本养老保险"))
                            {
                                if (tds.Count == 6)
                                {
                                    report.social_security.yanglaobx_num = tds[1].InnerText.Replace("\r", "")
                                        .Replace("\n", "").Replace("\t", "").Replace("<!---->", "").Replace(" ", "");
                                    report.social_security.dw_yanglaobx_js = tds[3].InnerText;
                                    report.social_security.bq_yanglaobx_je = tds[4].InnerText;
                                    report.social_security.dw_yanglaobx_je = tds[5].InnerText;
                                }
                            }
                            else if (tds.First().InnerText.Contains("失业保险"))
                            {
                                if (tds.Count == 6)
                                {
                                    report.social_security.shiyebx_num = tds[1].InnerText.Replace("\r", "")
                                        .Replace("\n", "").Replace("\t", "").Replace("<!---->", "").Replace(" ", "");
                                    report.social_security.dw_shiyebx_js = tds[3].InnerText;
                                    report.social_security.bq_shiyebx_je = tds[4].InnerText;
                                    report.social_security.dw_shiyebx_je = tds[5].InnerText;
                                }
                                else if (tds.Count == 3)
                                {
                                    report.social_security.shiyebx_num = report.social_security.yanglaobx_num;
                                    report.social_security.dw_shiyebx_js = report.social_security.dw_yanglaobx_js;
                                    report.social_security.bq_shiyebx_je = report.social_security.bq_yanglaobx_je;
                                    report.social_security.dw_shiyebx_je = report.social_security.dw_yanglaobx_je;
                                }
                            }
                            else if (tds.First().InnerText.Contains("职工基本医疗保险"))
                            {
                                if (tds.Count == 6)
                                {
                                    report.social_security.yiliaobx_num = tds[1].InnerText.Replace("\r", "")
                                        .Replace("\n", "").Replace("\t", "").Replace("<!---->", "").Replace(" ", ""); ;
                                    report.social_security.dw_yiliaobx_js = tds[3].InnerText;
                                    report.social_security.bq_yiliaobx_je = tds[4].InnerText;
                                    report.social_security.dw_yiliaobx_je = tds[5].InnerText;
                                }
                                else if (tds.Count == 3)
                                {
                                    report.social_security.yiliaobx_num = report.social_security.yanglaobx_num;
                                    report.social_security.dw_yiliaobx_js = report.social_security.dw_yanglaobx_js;
                                    report.social_security.bq_yiliaobx_je = report.social_security.bq_yanglaobx_je;
                                    report.social_security.dw_yiliaobx_je = report.social_security.dw_yanglaobx_je;
                                }
                            }
                            else if (tds.First().InnerText.Contains("工伤保险"))
                            {
                                if (tds.Count == 6)
                                {
                                    report.social_security.gongshangbx_num = tds[1].InnerText.Replace("\r", "")
                                        .Replace("\n", "").Replace("\t", "").Replace("<!---->", "").Replace(" ", ""); ;
                                    report.social_security.dw_gongshangbx_js = tds[3].InnerText;
                                    report.social_security.bq_gongshangbx_je = tds[4].InnerText;
                                    report.social_security.dw_gongshangbx_je = tds[5].InnerText;
                                }
                                else if (tds.Count == 3)
                                {
                                    report.social_security.gongshangbx_num = report.social_security.yanglaobx_num;
                                    report.social_security.dw_gongshangbx_js = report.social_security.dw_yanglaobx_js;
                                    report.social_security.bq_gongshangbx_je = report.social_security.bq_yanglaobx_je;
                                    report.social_security.dw_gongshangbx_je = report.social_security.dw_yanglaobx_je;
                                }
                            }
                            else if (tds.First().InnerText.Contains("生育保险"))
                            {
                                if (tds.Count == 6)
                                {
                                    report.social_security.shengyubx_num = tds[1].InnerText.Replace("\r", "")
                                        .Replace("\n", "").Replace("\t", "").Replace("<!---->", "").Replace(" ", "");
                                    report.social_security.dw_shengyubx_js = tds[3].InnerText;
                                    report.social_security.bq_shengyubx_je = tds[4].InnerText;
                                    report.social_security.dw_shengyubx_je = tds[5].InnerText;
                                }
                                else if (tds.Count == 3)
                                {
                                    report.social_security.shengyubx_num = report.social_security.yanglaobx_num;
                                    report.social_security.dw_shengyubx_js = report.social_security.dw_yanglaobx_js;
                                    report.social_security.bq_shengyubx_je = report.social_security.bq_yanglaobx_je;
                                    report.social_security.dw_shengyubx_je = report.social_security.dw_yanglaobx_je;
                                }
                            }
                        }
                    }
                }
            }
            // 网站或网店信息
            List<WebsiteItem> websiteList = new List<WebsiteItem>();
            int arrayStartIndex = responseData.IndexOf("swPaging(");
            if (arrayStartIndex != -1)
            {
                string temp = responseData.Substring(arrayStartIndex);
                int arrayEndIndex = temp.IndexOf("]");
                string websiteJsonArray = temp.Substring("swPaging(".Length, arrayEndIndex - "swPaging(".Length + 1);
                WebsiteLN[] jsonList = JsonConvert.DeserializeObject<WebsiteLN[]>(websiteJsonArray);
                int j = 1;
                foreach (WebsiteLN website in jsonList)
                {
                    WebsiteItem item = new WebsiteItem();

                    item.seq_no = j++;
                    item.web_type = website.typofwebName;
                    item.web_name = website.websitname;
                    item.web_url = website.domain;

                    websiteList.Add(item);
                }
            }
            report.websites = websiteList;

            // 股东及出资信息
            List<Partner> partnerList = new List<Partner>();
            arrayStartIndex = responseData.IndexOf("var  global_gsfrnbxqczxxJosnData=");
            if (arrayStartIndex != -1)
            {
                var temp = responseData.Substring(arrayStartIndex);
                var arrayEndIndex = temp.IndexOf("]");
                string reportPartnerJsonArray = temp.Substring("var  global_gsfrnbxqczxxJosnData=".Length, arrayEndIndex - "var  global_gsfrnbxqczxxJosnData=".Length + 1);
                ReportPartnerLN[] partnerJsonList = JsonConvert.DeserializeObject<ReportPartnerLN[]>(reportPartnerJsonArray);
                int j = 1;
                foreach (ReportPartnerLN partner in partnerJsonList)
                {
                    Partner item = new Partner();

                    item.seq_no = j++;
                    item.stock_name = partner.inv;
                    item.stock_type = "";
                    item.identify_no = "";
                    item.identify_type = "";
                    item.stock_percent = "";
                    item.ex_id = "";
                    item.real_capi_items = new List<RealCapiItem>();
                    item.should_capi_items = new List<ShouldCapiItem>();

                    ShouldCapiItem sItem = new ShouldCapiItem();
                    sItem.shoud_capi = string.IsNullOrEmpty(partner.lisubconam) ? "" : partner.lisubconam;
                    sItem.should_capi_date = partner.subcondatelabel;
                    sItem.invest_type = partner.subconformvalue;
                    item.should_capi_items.Add(sItem);

                    RealCapiItem rItem = new RealCapiItem();
                    rItem.real_capi = string.IsNullOrEmpty(partner.liacconam) ? "" : partner.liacconam;
                    rItem.real_capi_date = partner.accondatelabel;
                    rItem.invest_type = partner.acconformvalue;
                    item.real_capi_items.Add(rItem);

                    partnerList.Add(item);
                }
            }
            report.partners = partnerList;

            //对外投资信息
            List<InvestItem> investList = new List<InvestItem>();
            arrayStartIndex = responseData.IndexOf("var  global_gsfrnbxqtzxxJosnData=");
            if (arrayStartIndex != -1)
            {
                var temp = responseData.Substring(arrayStartIndex);
                var arrayEndIndex = temp.IndexOf("]");
                string investJsonArray = temp.Substring("var  global_gsfrnbxqtzxxJosnData=".Length, arrayEndIndex - "var  global_gsfrnbxqtzxxJosnData=".Length + 1);
                InvestLN[] investJsonList = JsonConvert.DeserializeObject<InvestLN[]>(investJsonArray);
                var j = 1;
                foreach (InvestLN invest in investJsonList)
                {
                    InvestItem item = new InvestItem();

                    item.seq_no = j++;
                    item.invest_name = invest.inventname;
                    item.invest_reg_no = invest.regno;

                    investList.Add(item);
                }
            }
            report.invest_items = investList;

            //股权变更
            arrayStartIndex = responseData.IndexOf("var  global_gsfrnbxqbgxxJosnData=");
            if (arrayStartIndex != -1)
            {
                var temp = responseData.Substring(arrayStartIndex);
                var arrayEndIndex = temp.IndexOf("]");
                string investJsonArray = temp.Substring("var  global_gsfrnbxqbgxxJosnData=".Length, arrayEndIndex - "var  global_gsfrnbxqbgxxJosnData=".Length + 1);
                ReportStockChangesLN[] rscJsonList = JsonConvert.DeserializeObject<ReportStockChangesLN[]>(investJsonArray);
                var j = 1;
                foreach (ReportStockChangesLN rsc in rscJsonList)
                {
                    StockChangeItem item = new StockChangeItem();

                    item.seq_no = j++;
                    item.before_percent = rsc.transbmpr;
                    item.after_percent = rsc.transampr;
                    item.name = rsc.inv;
                    item.change_date = rsc.altdatelabel;
                    report.stock_changes.Add(item);
                }
            }
            //修改信息
            arrayStartIndex = responseData.IndexOf("var  global_gsfrnbxqxgxxJosnData=");
            if (arrayStartIndex != -1)
            {
                var temp = responseData.Substring(arrayStartIndex);
                var arrayEndIndex = temp.IndexOf("]");
                string investJsonArray = temp.Substring("var  global_gsfrnbxqxgxxJosnData=".Length, arrayEndIndex - "var  global_gsfrnbxqxgxxJosnData=".Length + 1);
                ReportUpdateRecordLN[] ruJsonList = JsonConvert.DeserializeObject<ReportUpdateRecordLN[]>(investJsonArray);
                var j = 1;
                foreach (ReportUpdateRecordLN ru in ruJsonList)
                {
                    UpdateRecord item = new UpdateRecord();

                    item.seq_no = j++;
                    item.before_update = ru.altbe;
                    item.after_update = ru.altaf;
                    item.update_item = ru.alt;
                    item.update_date = ru.getAltdatevalue;
                    report.update_records.Add(item);
                }
            }
        }
        #endregion

        #region RemoveUnexpectedChar
        private string RemoveUnexpectedChar(string orginChar)
        {
            return orginChar.Replace(" ", "").Replace("\r", "").Replace("\n", "").Replace("\t", "");
        }
        #endregion

        #region SubstringJsonData
        private string  SubstringJsonData(string html)
        {
            var match = Regex.Match(html,"\\[.*?\\];");
            return match.Success ? match.Value.TrimEnd(';') : string.Empty;
        }
        #endregion

        #region SubstringJsonDataWithComma
        private string SubstringJsonDataWithComma(string html)
        {
            var match = Regex.Match(html, "\\[.*?\\],");
            return match.Success ? match.Value.TrimEnd(',') : string.Empty;
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
    }
}