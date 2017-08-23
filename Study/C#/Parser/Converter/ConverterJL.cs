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
using System.Net;
using iOubo.iSpider.Common;
using System.Collections.Specialized;
using System.Web;
using System.Configuration;
using iOubo.iSpider.Model.JiLin;
using MongoDB.Bson;

namespace iOubo.iSpider.Infrastructure.Parser
{
    public class ConverterJL : IConverter
    {
        DataRequest _request;
        RequestInfo _requestInfo;
        RequestXml _requestXml;
        EnterpriseInfo _enterpriseInfo = new EnterpriseInfo();
        List<AbnormalInfo> _abnormals = new List<AbnormalInfo>();
        List<CheckupInfo> _checkups = new List<CheckupInfo>();
        HtmlDocument hd = new HtmlDocument();
        List<string> reportsNeedToLoad = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings.Get("ReportsNeedToLoad"))
            ? new List<string>() : ConfigurationManager.AppSettings.Get("ReportsNeedToLoad").Split(',').ToList();
        int _parallelCount = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings.Get("ParallelCount")) ? 10 : int.Parse(ConfigurationManager.AppSettings.Get("ParallelCount"));
        public SummaryEntity ProcessRequestAndParse(RequestInfo requestInfo)
        {
            
            this._requestInfo = requestInfo;
            this._request = new DataRequest(requestInfo);
            if (requestInfo.Parameters.ContainsKey("entType"))
            {
                if (requestInfo.Parameters["entType"] == "Pb")
                {
                    if (requestInfo.Parameters.ContainsKey("platform") && "LIST_API" == requestInfo.Parameters["platform"])
                    {
                        this._requestXml = new RequestXml(requestInfo.CurrentPath, string.Format("{0}Gt_API", requestInfo.Province));
                    }
                    else
                    {
                        this._requestXml = new RequestXml(requestInfo.CurrentPath, string.Format("{0}Gt", requestInfo.Province));
                    }
                    
                }
                else if (requestInfo.Parameters["entType"] == "Sfc")
                {
                    if (requestInfo.Parameters.ContainsKey("platform") && "LIST_API" == requestInfo.Parameters["platform"])
                    {
                        this._requestXml = new RequestXml(requestInfo.CurrentPath, string.Format("{0}Sfc_API", requestInfo.Province));
                    }
                    else
                    {
                        this._requestXml = new RequestXml(requestInfo.CurrentPath, string.Format("{0}Sfc", requestInfo.Province));
                    }
                    
                }
                else
                {
                    if (requestInfo.Parameters.ContainsKey("platform") && "LIST_API" == requestInfo.Parameters["platform"])
                    {
                        this._requestXml = new RequestXml(requestInfo.CurrentPath, requestInfo.Province + "_API");
                    }
                    else
                    {
                        this._requestXml = new RequestXml(requestInfo.CurrentPath, requestInfo.Province);
                    }
                    
                }
            }
            else
            {
                this._requestXml = new RequestXml(requestInfo.CurrentPath, requestInfo.Province);
            }
            InitialEnterpriseInfo();

            //解析基本信息
          //  _requestInfo.Referer = string.Format("http://211.141.74.200/Publicity/Details.html?id={0}&entTypeCode={1}", requestInfo.Parameters["id"], requestInfo.Parameters["entType"]);
            List<ResponseInfo> responseList = _request.GetResponseInfo(_requestXml.GetRequestListByGroup("basic"));
            Parallel.ForEach(responseList, new ParallelOptions { MaxDegreeOfParallelism = _parallelCount }, oneRespon => this.LoadAndParaseData(oneRespon));

            if (_enterpriseInfo.econ_kind == "个人独资企业")
            {
                _enterpriseInfo.employees = new List<Employee>();
            }
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
        private void LoadAndParaseData(ResponseInfo item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Data)) return;

            if (item.Name == "basicInfo")
            {
                this.LoadAndParseBasic(item.Data);
            }
            else if (item.Name == "partnerInfo")
            {
                this.LoadAndParsePartner(item.Data);
            }
            else if (item.Name == "employeeInfo")
            {
                this.LoadAndParseEmployee(item.Data);
            }
            else if (item.Name == "branchInfo")
            {
                this.LoadAndParseBranch(item.Data);
            }
            else if (item.Name == "changerecordInfo")
            {
                this.LoadAndParseChangeRecords(item.Data);
            }
            else if (item.Name == "licenceInfo")
            {
                this.LoadAndParseLicence(item.Data);
            }
            else if (item.Name == "licenceInfo_gs")
            {
                this.LoadAndParseLicence(item.Data);
            }
            else if (item.Name == "abnormalInfo")
            {
                this.LoadAndParseAbnormal(item.Data);
            }
            else if (item.Name == "checkupInfo")
            {
                this.LoadAndParseCheckups(item.Data);
            }
            else if (item.Name == "mortgageInfo")
            {
                this.LoadAndParseMortgage(item.Data);
            }
            else if (item.Name == "equity_qualityInfo")
            {
                this.LoadAndParseEquityQuality(item.Data);
            }
            else if (item.Name == "administrative_punishmentInfo_gs")
            {
                this.LoadAndParseAdministivePunishment(item.Data);
            }
            else if (item.Name == "administrative_punishmentInfo")
            {
                this.LoadAndParseAdministivePunishment(item.Data);
            }
            else if (item.Name == "financial_contributionInfo")
            {
                this.LoadAndParseFinancialContribution(item.Data);
            }
            else if (item.Name == "stock_changeInfo")
            {
                this.LoadAndParseStockChange(item.Data);
            }
            else if (item.Name == "elicenceInfo")
            {
                this.LoadAndParseLicence_Enterprise(item.Data);
            }
            else if (item.Name == "knowledge_propertyInfo")
            {
                this.LoadAndParseknowledge_property(item.Data);
            }
            else if (item.Name == "reportyeartlist")
            {
                this.LoadAndParseReportList(item.Data);
            }
            else if (item.Name == "judicial_freezeInfo")
            {
                this.LoadAndParseJudicialFreeze(item.Data);
            }
        }

        #region 解析基本信息
        /// <summary>
        /// 解析登记信息：基本信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseBasic(string responseData)
        {

            if (_requestInfo.Parameters.ContainsKey("entType"))
            {
                //  if (_requestInfo.Parameters["entType"] == "Ent")
                //  {
                var info = JsonConvert.DeserializeObject<BasicInfo_JL>(responseData);
                if (info != null)
                {
                    Utility.ClearNullValue<BasicInfo_JL>(info);
                    _enterpriseInfo.name = info.entName;
                    _enterpriseInfo.reg_no = info.regNo;
                    _enterpriseInfo.credit_no = info.uniscId;
                    _enterpriseInfo.econ_kind = info.entType_CN;
                    _enterpriseInfo.oper_name = info.leRep;
                    //_enterpriseInfo.regist_capi = string.Format("{0}万元{1}", info.regCap, info.currency_CN);
                    _enterpriseInfo.regist_capi = info.regCap;
                    _enterpriseInfo.start_date = info.estDate;
                    _enterpriseInfo.term_start = info.opFrom;
                    _enterpriseInfo.term_end = info.opTo;
                    _enterpriseInfo.belong_org = info.regOrg_CN;
                    _enterpriseInfo.check_date = info.apprDate;
                    _enterpriseInfo.addresses.Add(new Address { name = "注册地址", address = string.IsNullOrEmpty(info.opLoc)?info.dom:info.opLoc });
                    _enterpriseInfo.status = info.regState_CN;
                    _enterpriseInfo.scope =(string.IsNullOrEmpty(info.opScoType_CN)|| info.opScoType_CN=="其他") ?info.opScope:info.opScoType_CN ;
                }
            }
             //   }
            //    else if (_requestInfo.Parameters["entType"] == "Pb")
            //    {
            //        var info = JsonConvert.DeserializeObject<BasicInfo_GT_JL>(responseData);
            //        if (info != null)
            //        {
            //            Utility.ClearNullValue<BasicInfo_GT_JL>(info);
            //            _enterpriseInfo.reg_no = info.regNo;
            //            _enterpriseInfo.credit_no = info.uniscId;
            //            _enterpriseInfo.name = info.traName;
            //            _enterpriseInfo.econ_kind = info.entType_CN;
            //            _enterpriseInfo.oper_name = info.name;
            //            _enterpriseInfo.addresses.Add(new Address { name = "注册地址", address = info.opLoc });
            //            _enterpriseInfo.type_desc = info.compForm_CN;
            //            _enterpriseInfo.start_date = info.estDate;
            //            _enterpriseInfo.belong_org = info.regOrg_CN;
            //            _enterpriseInfo.check_date = info.apprDate;
            //            _enterpriseInfo.status = info.regState_CN;
            //            _enterpriseInfo.scope = info.opsCope;
            //        }
            //    }
            //}
        }
        #endregion

        #region 解析股东信息
        /// <summary>
        /// 解析股东信息
        /// </summary>
        /// <param name="responseData"></param>
        private void LoadAndParsePartner(string responseData)
        {
            var info = JsonConvert.DeserializeObject<PartnerInfo_JL>(responseData);
            if (info != null && info.data!=null && info.data.Any())
            {
                foreach (var item in info.data)
                {
                    Utility.ClearNullValue<PartnerItem_JL>(item);
                    Partner partner = new Partner();
                    partner.seq_no = _enterpriseInfo.partners.Count + 1;
                    partner.stock_name = item.inv;
                    partner.stock_type = item.invType_CN;
                    partner.identify_type = item.blicType_CN;
                    partner.identify_no = item.blicNO;
                    this.LoadAndParsePartnerDetail(item.invId, partner);
                    _enterpriseInfo.partners.Add(partner);
                }
            }
        }
        
        #endregion

        #region 解析股东详情
        /// <summary>
        /// 解析股东详情
        /// </summary>
        /// <param name="invId"></param>
        /// <param name="partner"></param>
        private void LoadAndParsePartnerDetail(string invId,Partner partner)
        {
            var request = this.CreateRequest();
            request.AddOrUpdateRequestParameter("pripId",invId);
            var responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("partnerInfo"));

            if (responseList != null && responseList.Any())
            {
                ShouldCapiItem t_sci = new ShouldCapiItem();
                RealCapiItem t_rci = new RealCapiItem();

                foreach (var responseInfo in responseList)
                {
                    if (responseInfo == null) continue;
                    if (responseInfo.Name == "partnerInfo_detail")
                    {
                        if (string.IsNullOrWhiteSpace(responseInfo.Data)) return;
                        var info = JsonConvert.DeserializeObject<PartnerDetailInfo_JL>(responseInfo.Data);
                        if (info != null)
                        {
                            Utility.ClearNullValue<PartnerDetailInfo_JL>(info);
                            partner.total_should_capi = info.liSubConAm;
                            partner.total_real_capi = info.liAcConAm;
                        }
                    }
                    else if (responseInfo.Name == "partnerInfo_detail_subcon")
                    {
                        if (string.IsNullOrWhiteSpace(responseInfo.Data)) return;
                        var info = JsonConvert.DeserializeObject<PartnerDetailSunConInfo_JL>(responseInfo.Data);
                        if (info != null && info.data != null && info.data.Any())
                        {
                            foreach (var item in info.data)
                            {
                                Utility.ClearNullValue<PartnerDetailSunConItem_JL>(item);
                                ShouldCapiItem sci = new ShouldCapiItem();
                                sci.invest_type = item.subConForm_CN;
                                sci.shoud_capi = item.subConAm;
                                sci.should_capi_date = item.subConDate;
                                partner.should_capi_items.Add(sci);
                            }
                        }
                    }
                    else if (responseInfo.Name == "partnerInfo_detail_accon")
                    {
                        if (string.IsNullOrWhiteSpace(responseInfo.Data)) return;
                        var info = JsonConvert.DeserializeObject<PartnerDetailAcConInfo_JL>(responseInfo.Data);
                        if (info != null && info.data != null && info.data.Any())
                        {
                            foreach (var item in info.data)
                            {
                                Utility.ClearNullValue<PartnerDetailAcConItem_JL>(item);
                                RealCapiItem rci = new RealCapiItem();
                                rci.invest_type = item.acConForm_CN;
                                rci.real_capi = item.acConAm;
                                rci.real_capi_date = item.conDate;
                                partner.real_capi_items.Add(rci);
                            }
                        }
                    }
                }
                if (!partner.should_capi_items.Any() && !string.IsNullOrWhiteSpace(t_sci.shoud_capi))
                {
                    partner.should_capi_items.Add(t_sci);
                }
                if (!partner.real_capi_items.Any() && !string.IsNullOrWhiteSpace(t_rci.real_capi))
                {
                    partner.real_capi_items.Add(t_rci);
                }
            }
        }
        #endregion

        #region 解析主要人员信息
        /// <summary>
        /// 解析主要人员信息
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseEmployee(string responseData)
        {
            var info = JsonConvert.DeserializeObject<EmployeeInfo_JL>(responseData);
            if (info != null && info.data != null && info.data.Any())
            {
                foreach (var item in info.data)
                {
                    Utility.ClearNullValue<EmployeeItem_JL>(item);
                    Employee employee = new Employee();
                    employee.seq_no = _enterpriseInfo.employees.Count + 1;
                    employee.name = item.name;
                    employee.job_title = item.position_CN;
                    _enterpriseInfo.employees.Add(employee);
                }
            }
        }
        #endregion

        #region 解析分支机构
        /// <summary>
        /// 解析备案信息：分支机构
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseBranch(string responseData)
        {
            var info = JsonConvert.DeserializeObject<BranchInfo_JL>(responseData);
            //if (info != null && info.data != null && info.data.Any())
            //{
            //    foreach (var item in info.data)
            //    {
            //        Utility.ClearNullValue<BranchItem_JL>(item);
            //        Employee employee = new Employee();
            //        employee.seq_no = _enterpriseInfo.employees.Count + 1;
            //        employee.name = item.name;
            //        employee.job_title = item.position_CN;
            //        _enterpriseInfo.employees.Add(employee);
            //    }
            //} 
        }
        #endregion

        #region 解析变更信息
        /// <summary>
        /// 解析变更信息
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseChangeRecords(string responseData)
        {
            var info = JsonConvert.DeserializeObject<ChangeRecordInfo_JL>(responseData);
            if (info != null && info.data != null && info.data.Any())
            {
                foreach (var item in info.data)
                {
                    Utility.ClearNullValue<ChangeRecordItem_JL>(item);
                    ChangeRecord changerecord = new ChangeRecord();
                    changerecord.seq_no = _enterpriseInfo.changerecords.Count + 1;
                    changerecord.change_item = item.altItem_CN;
                    changerecord.before_content = item.altBe;
                    changerecord.after_content = item.altAf;
                    changerecord.change_date = item.altDate;
                    _enterpriseInfo.changerecords.Add(changerecord);
                }
            }
        }
        #endregion

        #region 解析行政许可信息
        /// <summary>
        /// 解析行政许可信息
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseLicence(string responseData)
        {
            var info = JsonConvert.DeserializeObject<LicenceInfo_JL>(responseData);
            if (info != null && info.data != null && info.data.Any())
            {
                foreach (var item in info.data)
                {
                    Utility.ClearNullValue<LicenceItem_JL>(item);
                    LicenseInfo license = new LicenseInfo();
                    license.seq_no = _enterpriseInfo.licenses.Count + 1;
                    license.number = item.licNo;
                    license.name = item.licName_CN;
                    license.start_date = item.valFrom;
                    license.end_date = item.valTo;
                    license.department = item.licAnth;
                    license.content = item.licItem;
                    license.status = item.status == "1" ? "有效" : "无效";
                    _enterpriseInfo.licenses.Add(license);
                }
            }
        }
        #endregion

        #region 解析经营异常信息
        /// <summary>
        /// 解析经营异常信息
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseAbnormal(string responseData)
        {
            var info = JsonConvert.DeserializeObject<AbnormalInfo_JL>(responseData);
            if (info != null && info.data != null && info.data.Any())
            {
                foreach (var item in info.data)
                {
                    Utility.ClearNullValue<AbnormalItem_JL>(item);
                    AbnormalInfo abnormalInfo = new AbnormalInfo();
                    abnormalInfo.name = _enterpriseInfo.name;
                    abnormalInfo.reg_no = string.IsNullOrWhiteSpace(_enterpriseInfo.reg_no) ? _enterpriseInfo.credit_no : _enterpriseInfo.reg_no;
                    abnormalInfo.province = _enterpriseInfo.province;
                    abnormalInfo.in_reason = item.speCause_CN;
                    abnormalInfo.in_date = item.abntime;
                    abnormalInfo.department = item.decOrg_CN;
                    abnormalInfo.out_reason = item.remExcpRes_CN;
                    abnormalInfo.out_date = item.remDate;
                    _abnormals.Add(abnormalInfo);
                }
            }
        }
        #endregion

        #region 解析抽查检查信息
        /// <summary>
        /// 解析抽查检查信息
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseCheckups(string responseData)
        {
            var info = JsonConvert.DeserializeObject<CheckupInfo_JL>(responseData);
            if (info != null && info.data != null && info.data.Any())
            {
                foreach (var item in info.data)
                {
                    Utility.ClearNullValue<CheckupItem_JL>(item);
                    CheckupInfo checkupInfo = new CheckupInfo();
                    checkupInfo.name = _enterpriseInfo.name;
                    checkupInfo.reg_no = string.IsNullOrWhiteSpace(_enterpriseInfo.reg_no) ? _enterpriseInfo.credit_no : _enterpriseInfo.reg_no;
                    checkupInfo.province = _enterpriseInfo.province;
                    checkupInfo.department = item.insAuth_CN;
                    checkupInfo.type = item.insType == "1" ? "抽查" : "检查";
                    checkupInfo.date = item.insDate;
                    checkupInfo.result = item.insRes_CN;
                    _checkups.Add(checkupInfo);
                }
            }
        }
        #endregion

        #region 解析动产抵押信息
        /// <summary>
        /// 解析动产抵押信息
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseMortgage(string responseData)
        {
            var info = JsonConvert.DeserializeObject<MortgageInfo_JL>(responseData);
            if (info != null && info.data != null && info.data.Any())
            {
                foreach (var item in info.data)
                {
                    Utility.ClearNullValue<MortgageItem_JL>(item);
                    MortgageInfo mortgageInfo = new MortgageInfo();
                    mortgageInfo.seq_no = _enterpriseInfo.mortgages.Count + 1;
                    mortgageInfo.number = item.morRegCNO;
                    mortgageInfo.date = item.regiDate;
                    mortgageInfo.department = item.regOrg_Cn;
                    mortgageInfo.amount = item.priClaSecAm;
                    mortgageInfo.public_date = item.publicDate;
                    mortgageInfo.status = item.type == "1" ? "有效" : "无效";
                    LoadAndParseMortgageDetail(item.morreg_Id, mortgageInfo);
                    _enterpriseInfo.mortgages.Add(mortgageInfo);
                }
            }
        }
        #endregion

        #region 解析动产抵押详情
        /// <summary>
        /// 解析动产抵押详情
        /// </summary>
        /// <param name="morreg_Id"></param>
        /// <param name="mortgageInfo"></param>
        void LoadAndParseMortgageDetail(string morreg_Id, MortgageInfo mortgageInfo)
        {
            var request = this.CreateRequest();
            request.AddOrUpdateRequestParameter("pripId", morreg_Id);
            var responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("mortgageInfo"));
            if (responseList != null && responseList.Any())
            {
                foreach (var responseInfo in responseList)
                {
                    if (string.IsNullOrWhiteSpace(responseInfo.Data)) continue;
                    
                    if (responseInfo.Name == "mortgageInfo_detail")
                    {
                        var info = JsonConvert.DeserializeObject<MortgageItem_Detail_JL>(responseInfo.Data);
                        Utility.ClearNullValue<MortgageItem_Detail_JL>(info);

                        mortgageInfo.debit_type = info.priClaSecKind_CN;

                        mortgageInfo.debit_amount = info.priClaSecAm;

                        mortgageInfo.debit_scope = info.warCov;

                        mortgageInfo.debit_period = info.pefPerForm + "至" + info.pefPerTo;

                        mortgageInfo.debit_remarks = info.remark;
      
                    }
                    else if (responseInfo.Name == "mortgageInfo_dyqr")
                    {
                        var infos = JsonConvert.DeserializeObject<MortgageInfo_DYQR_Arr_JL>(responseInfo.Data);
                        foreach(var info in infos.data)
                        {
                            mortgageInfo.mortgagees.Add(new Mortgagee()
                                {
                                    seq_no = mortgageInfo.mortgagees.Count+1,
                                    identify_no = string.IsNullOrWhiteSpace(info.BlicNO) ? string.Empty : info.BlicNO,
                                    identify_type = string.IsNullOrWhiteSpace(info.BlicType_CN) ? string.Empty : info.BlicType_CN,
                                    name = info.More

                                });
                        }
                    }
                    else if (responseInfo.Name == "mortgageInfo_dyw")
                    {
                        var infos = JsonConvert.DeserializeObject<MortgageInfo_DYW_Arr_JL>(responseInfo.Data);
                        foreach(var info in infos.data)
                        {
                            mortgageInfo.guarantees.Add(new Guarantee()
                                {
                                    seq_no = mortgageInfo.guarantees.Count + 1,
                                    name = info.guaName,
                                    belong_to = info.own,
                                    desc = info.guaDes,
                                    remarks = info.remark
                                });
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析股权出质
        void LoadAndParseEquityQuality(string responseData)
        {
            var info = JsonConvert.DeserializeObject<EquityQualityInfo_JL>(responseData);
            if (info != null && info.data != null && info.data.Any())
            {
                foreach (var item in info.data)
                {
                    var unit = item.pledAmUnit == "1" ? "万元" : "万股";
                    Utility.ClearNullValue<EquityQualityItem_JL>(item);
                    EquityQuality equityQuality = new Model.EquityQuality();
                    equityQuality.seq_no = _enterpriseInfo.equity_qualities.Count + 1;
                    equityQuality.number = item.equityNo;
                    equityQuality.pledgor = item.pledgor;
                    equityQuality.pledgor_identify_no = item.pledgorBLicNO;
                    //equityQuality.pledgor_amount = string.Format("{0}{1}", item.impAm, unit);
                    equityQuality.pledgor_amount = item.impAm;
                    equityQuality.pawnee = item.impOrg;
                    equityQuality.pawnee_identify_no = item.impOrgBLicNO;
                    equityQuality.date= item.equPleDate;
                    equityQuality.public_date = item.publicDate;
                    equityQuality.status = item.status == "1" ? "有效" : "无效";
                    equityQuality.pledgor_unit = item.pledAmUnit == "1" ? "万元" : "万股";
                    _enterpriseInfo.equity_qualities.Add(equityQuality);
                }
            }
        }
        #endregion

        #region 解析股东及出资--企业
        /// <summary>
        /// 解析股东及出资
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseFinancialContribution(string responseData)
        {
            var info = JsonConvert.DeserializeObject<FinancialContributionInfo_JL>(responseData);
            if (info != null && info.data != null && info.data.Any())
            {
                foreach (var item in info.data)
                {
                    Utility.ClearNullValue<FinancialContributionItem_JL>(item);
                    FinancialContribution fc = new FinancialContribution();
                    fc.seq_no = _enterpriseInfo.financial_contributions.Count + 1;
                    fc.investor_name = item.inv;
                    fc.total_should_capi = item.totalSubConAm;
                    fc.total_real_capi = item.totalAcConAm;
                    FinancialContribution.ShouldCapiItem sci = new FinancialContribution.ShouldCapiItem();
                    sci.should_invest_type = item.subConForm_CN;
                    sci.should_capi = item.subConAm;
                    sci.should_invest_date = item.currency;
                    sci.public_date = item.shouldPublicDate;
                    fc.should_capi_items.Add(sci);

                    FinancialContribution.RealCapiItem rci = new FinancialContribution.RealCapiItem();
                    rci.real_invest_type = item.acConForm_CN;
                    rci.real_capi = item.acConAm;
                    rci.real_invest_date = item.conDate;
                    rci.public_date = item.factPublicDate;
                    fc.real_capi_items.Add(rci);
                    
                    _enterpriseInfo.financial_contributions.Add(fc);
                }
            }
        }
        #endregion

        #region 解析股权变更--企业
        /// <summary>
        /// 解析股权变更
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseStockChange(string responseData)
        {
            var info = JsonConvert.DeserializeObject<StockChangeInfo_JL>(responseData);
            if (info != null && info.data != null && info.data.Any())
            {
                foreach (var item in info.data)
                {
                    Utility.ClearNullValue<StockChangeItem_JL>(item);
                    StockChangeItem sci = new StockChangeItem();
                    sci.seq_no = _enterpriseInfo.stock_changes.Count + 1;
                    sci.name = item.inv;
                    sci.before_percent = item.transAmPrBf;
                    sci.after_percent = item.transAmPrAf;
                    sci.change_date = item.altDate;
                    sci.public_date = item.publicDate;
                    _enterpriseInfo.stock_changes.Add(sci);
                }
            }
        }
        #endregion

        #region 解析行政许可--企业
        /// <summary>
        /// 解析行政许可--企业
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseLicence_Enterprise(string responseData)
        {
            var info = JsonConvert.DeserializeObject<LicenceInfo_JL>(responseData);
            if (info != null && info.data != null && info.data.Any())
            {
                foreach (var item in info.data)
                {
                    Utility.ClearNullValue<LicenceItem_JL>(item);
                    LicenseInfo license = new LicenseInfo();
                    license.seq_no = _enterpriseInfo.licenses.Count + 1;
                    license.number = item.licNo;
                    license.name = item.licName_CN;
                    license.start_date = item.valFrom;
                    license.end_date = item.valTo;
                    license.department = item.licAnth;
                    license.content = item.licItem;
                    license.status = item.status == "1" ? "有效" : "无效";
                    _enterpriseInfo.licenses.Add(license);
                }
            }
        }
        #endregion

        #region 解析知识产权出质登记信息--企业
        /// <summary>
        /// 解析知识产权出质登记信息--企业
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseknowledge_property(string responseData)
        {
            var info = JsonConvert.DeserializeObject<Knowledge_propertyInfo_JL>(responseData);
            if (info != null && info.data != null && info.data.Any())
            {
                foreach (var item in info.data)
                {
                    Utility.ClearNullValue<Knowledge_propertyItem_JL>(item);
                    KnowledgeProperty kp = new KnowledgeProperty();
                    kp.seq_no = _enterpriseInfo.knowledge_properties.Count + 1;
                    kp.number = item.tmRegNo;
                    kp.name = item.tmName;
                    if (item.kinds == "1")
                    {
                        kp.type = "商标";
                    }
                    else if (item.kinds == "2")
                    {
                        kp.type = "版权";
                    }
                    else 
                    {
                        kp.type = "专利";
                    }
                    kp.pledgor = item.pledgor;
                    kp.pawnee = item.impOrg;
                    kp.period = item.pleregper;
                    kp.status = item.type == "1" ? "有效" : "无效";
                    kp.public_date = item.publicDate;
                    _enterpriseInfo.knowledge_properties.Add(kp);
                }
            }
        }
        #endregion

        #region 解析行政处罚信息--企业
        /// <summary>
        /// 解析行政处罚信息--企业
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseAdministivePunishment(string responseData)
        {
            var info = JsonConvert.DeserializeObject<Administrative_punishmentInfo_JL>(responseData);
            if (info != null && info.data != null && info.data.Any())
            {
                foreach (var item in info.data)
                {
                    Utility.ClearNullValue<Administrative_punishmentItem_JL>(item);
                    AdministrativePunishment ap = new AdministrativePunishment();
                    ap.seq_no = _enterpriseInfo.administrative_punishments.Count + 1;
                    ap.number = item.pendecNo;
                    ap.illegal_type = item.illegactType;
                    ap.department = item.judauth;
                    ap.content = item.penType_Cn;
                    ap.date = item.pendEcissDate;
                    ap.remark = item.remark;
                    ap.name = item.entName;
                    ap.oper_name = _enterpriseInfo.oper_name;
                    ap.reg_no = item.regNo;
                    ap.public_date = item.publicDate;
                    _enterpriseInfo.administrative_punishments.Add(ap);
                }
            }
        }
        #endregion

        #region 解析股权冻结信息--企业
        /// <summary>
        /// 解析股权冻结信息--企业
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseJudicialFreeze(string responseData)
        {
            var info = JsonConvert.DeserializeObject<Judicial_freezeInfo_JL>(responseData);
            if (info != null && info.data != null && info.data.Any())
            {
                foreach (var item in info.data)
                {
                    Utility.ClearNullValue<Judicial_freezeItem_JL>(item);
                    JudicialFreeze jf = new JudicialFreeze();
                    jf.seq_no = _enterpriseInfo.judicial_freezes.Count + 1;
                    jf.be_executed_person = item.inv;
                    jf.amount = item.fromAm;
                    jf.executive_court = item.froAuth;
                    jf.number = item.executeNo;
                    jf.status = item.frozState_CN;

                    _enterpriseInfo.judicial_freezes.Add(jf);
                }
            }
        }
        #endregion

        #region 解析年报列表
        void LoadAndParseReportList(string responseData)
        {
            var request = this.CreateRequest();
            var info = JsonConvert.DeserializeObject<ReportInfo>(responseData);
            foreach (var row in info.data)
            {
                request.AddOrUpdateRequestParameter("reportYear", row.ancheId);
                var responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("reportyeartlist"));
                if (responseList != null && responseList.Any())
                {
                    if (_requestInfo.Parameters.ContainsKey("entType"))
                    {
                        if (_requestInfo.Parameters["entType"] == "Pb")
                        {
                            this.LoadAndParseReport_GT(string.Empty);
                        }
                        else if (_requestInfo.Parameters["entType"] == "Sfc")
                        {
                            this.LoadAndParseReport_SFC(responseList);
                        }
                        else
                        {
                            this.LoadAndParseReport(responseList);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析年报信息211.141.74.200
        /// <summary>
        /// 解析年报信息
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseReport(List<ResponseInfo> responses)
        {
            Report report = new Report();
            foreach (var response in responses)
            {
                if (response.Name == "reportInfo")
                {
                    var info = JsonConvert.DeserializeObject<ReportInfo_JL>(response.Data);
                    if (info != null)
                    {
                        Utility.ClearNullValue<ReportInfo_JL>(info);

                        report.report_year = info.ancheYear;
                        report.report_name = string.Format("{0}年度报告", info.ancheYear);
                        report.report_date = info.ancheDate;
                        report.reg_no = info.uniscId.Length == 18 ? string.Empty : info.uniscId;
                        report.credit_no = info.uniscId.Length == 18 ? info.uniscId : string.Empty;
                        report.name = info.entName;
                        report.telephone = info.tel;
                        report.zip_code = info.postalCode;
                        report.address = info.addr;
                        report.email = info.email;
                        report.collegues_num = string.IsNullOrWhiteSpace(info.empNum) ? string.Empty : string.Format("{0}", info.empNum);
                        report.status = info.busst_CN;
                        report.if_website = info.hasWebSite;
                        report.if_invest = info.hasForInvestment;
                        report.if_equity = info.hasAlterStock;
                    }
                }
                else if (response.Name == "reportAssetInfo")
                {
                    var info = JsonConvert.DeserializeObject<Report_Asset_JL>(response.Data);
                    if (info != null)
                    {
                        Utility.ClearNullValue<Report_Asset_JL>(info);
                        report.total_equity = info.assGro;
                        report.profit_reta = info.totEqu;
                        report.sale_income = info.vendInc;
                        report.profit_total = info.proGro;
                        report.serv_fare_income = info.maiBusInc;
                        report.net_amount = info.netInc;
                        report.tax_total = info.ratGro;
                        report.debit_amount = info.liaGro;
                    }
                }

                else if (response.Name == "reportWebsiteInfo")
                {
                    var info = JsonConvert.DeserializeObject<WebsiteInfo_Report_JL>(response.Data);
                    if (info != null)
                    {
                        foreach (var item in info.data)
                        {
                            Utility.ClearNullValue<WebsiteItem_Report_JL>(item);
                            WebsiteItem website = new WebsiteItem();
                            website.seq_no = report.websites.Count + 1;
                            website.web_name = item.webSitName;
                            website.web_type = item.webType;
                            website.web_url = item.domain;
                            report.websites.Add(website);
                        }
                    }
                }
                else if (response.Name == "reportPartnersInfo")
                {
                    var info = JsonConvert.DeserializeObject<PartnerInfo_Report_JL>(response.Data);
                    if (info != null)
                    {
                        foreach (var item in info.data)
                        {
                            Utility.ClearNullValue<PartnerItem_Report_JL>(item);
                            Partner partner = new Partner();
                            partner.seq_no = report.partners.Count + 1;
                            partner.stock_name = item.invName;
                            ShouldCapiItem sci = new ShouldCapiItem();
                            sci.invest_type = item.subConForm_CN;
                            sci.shoud_capi = item.liSubConAm;
                            sci.should_capi_date = item.subConDate;
                            partner.should_capi_items.Add(sci);
                            RealCapiItem rci = new RealCapiItem();
                            rci.invest_type = item.acConForm_CN;
                            rci.real_capi = item.liAcConAm;
                            rci.real_capi_date = item.acConDate;
                            partner.real_capi_items.Add(rci);
                            report.partners.Add(partner);
                        }
                    }
                }
                else if (response.Name == "reportInvestmentsInfo")
                {
                    var info = JsonConvert.DeserializeObject<InvestInfo_Report_JL>(response.Data);
                    if (info != null)
                    {
                        foreach (var item in info.data)
                        {
                            Utility.ClearNullValue<InvestItem_Report_JL>(item);
                            InvestItem invest = new InvestItem();
                            invest.seq_no = report.invest_items.Count + 1;
                            invest.invest_name = item.entName;
                            invest.invest_reg_no = item.uniscId;
                            report.invest_items.Add(invest);
                        }
                    }
                }
                else if (response.Name == "reportGuaranteesInfo")
                {
                    var info = JsonConvert.DeserializeObject<ExternalGuaranteeInfo_Report_JL>(response.Data);
                    if (info != null)
                    {
                        foreach (var item in info.data)
                        {
                            Utility.ClearNullValue<ExternalGuaranteeItem_Report_JL>(item);
                            ExternalGuarantee eg = new ExternalGuarantee();
                            eg.seq_no = report.external_guarantees.Count + 1;
                            eg.creditor = item.more;
                            eg.debtor = item.mortgagor;
                            eg.type = item.priClaSecKind;
                            eg.amount = string.IsNullOrWhiteSpace(item.priClaSecAm) ? item.priClaSecAm : string.Format("{0}", item.priClaSecAm);
                            eg.period = string.Format("{0}至{1}", item.pefPerForm, item.pefPerTo);
                            eg.guarantee_time = item.guaranperiod;
                            eg.guarantee_type = item.gaType;
                            
                            report.external_guarantees.Add(eg);
                        }
                    }
                }
                else if (response.Name == "reportAlterStocksInfo")
                {
                    var info = JsonConvert.DeserializeObject<StockChangeInfo_Report_JL>(response.Data);
                    if (info != null)
                    {
                       foreach (var item in info.data)
                       {
                           Utility.ClearNullValue<StockChangeItem_Report_JL>(item);
                           StockChangeItem updateRecord = new StockChangeItem();
                           updateRecord.seq_no = report.stock_changes.Count + 1;
                           updateRecord.name = item.inv;
                           updateRecord.before_percent = item.transAmPrBf;
                           updateRecord.after_percent = item.transAmPrAf;
                           updateRecord.change_date = item.altDate;
                           report.stock_changes.Add(updateRecord);
                       }
                    }
                }
                else if (response.Name == "reportAnUpdatesInfo")
                {
                    var info = JsonConvert.DeserializeObject<UpdateRecordInfo_Report_JL>(response.Data);
                    if (info != null)
                    {
                        foreach (var item in info.data)
                        {
                            Utility.ClearNullValue<UpdateRecordItem_Report_JL>(item);
                            UpdateRecord updateRecord = new UpdateRecord();
                            updateRecord.seq_no = report.update_records.Count + 1;
                            updateRecord.update_item = item.alitem;
                            updateRecord.before_update = item.altBe;
                            updateRecord.after_update= item.altAf;
                            updateRecord.update_date = item.altDate;
                            report.update_records.Add(updateRecord);
                        }
                    }
                }
                else if (response.Name == "reportSheBaoInfo")
                {
                    var info = JsonConvert.DeserializeObject<SocialSecurityInfo_Report_JL>(response.Data);
                    if (info != null)
                    {
                        Utility.ClearNullValue<SocialSecurityInfo_Report_JL>(info);
                        report.social_security.yanglaobx_num = info.SO110;
                        report.social_security.shiyebx_num = info.SO210;
                        report.social_security.yiliaobx_num = info.SO310;
                        report.social_security.gongshangbx_num = info.SO410;
                        report.social_security.shengyubx_num = info.SO510;
                        report.social_security.dw_yanglaobx_js = info.TOTALWAGES_SO110;
                        report.social_security.dw_shiyebx_js = info.TOTALWAGES_SO210;
                        report.social_security.dw_yiliaobx_js = info.TOTALWAGES_SO310;
                        report.social_security.dw_shengyubx_js = info.TOTALWAGES_SO510;
                        report.social_security.bq_yanglaobx_je = info.TOTALPAYMENT_SO110;
                        report.social_security.bq_shiyebx_je = info.TOTALPAYMENT_SO210;
                        report.social_security.bq_yiliaobx_je = info.TOTALPAYMENT_SO310;
                        report.social_security.bq_gongshangbx_je = info.TOTALPAYMENT_SO410;
                        report.social_security.bq_shengyubx_je = info.TOTALPAYMENT_SO510;
                        report.social_security.dw_yanglaobx_je = info.UNPAIDSOCIALINS_SO110;
                        report.social_security.dw_shiyebx_je = info.UNPAIDSOCIALINS_SO210;
                        report.social_security.dw_yiliaobx_je = info.UNPAIDSOCIALINS_SO310;
                        report.social_security.dw_gongshangbx_je = info.UNPAIDSOCIALINS_SO410;
                        report.social_security.dw_shengyubx_je = info.UNPAIDSOCIALINS_SO510;
                    }
                }
            }
            _enterpriseInfo.reports.Add(report);

        }
        #endregion

        #region 解析年报信息--个体
        /// <summary>
        /// 解析年报信息--个体
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseReport_GT(string responseData)
        {
            var info = JsonConvert.DeserializeObject<ReportInfo_GT_JL>(responseData);
            if (info != null)
            {
                Utility.ClearNullValue<ReportInfo_GT_JL>(info);
                Report report = new Report();
                report.report_year = info.anCheYear;
                report.report_name = string.Format("{0}年度报告", info.anCheYear);
                report.report_date = info.ancheDate;
                report.reg_no = info.regNo.Length == 18 ? string.Empty : info.regNo;
                report.credit_no = info.regNo.Length == 18 ? info.regNo : string.Empty;
                report.name = info.traName;
                report.oper_name = info.name;
                report.telephone = info.tel;
                report.reg_capi = string.Format("{0}",info.fundAm);
                report.zip_code = "";
                report.address = "";
                report.email = "";
                report.collegues_num = string.IsNullOrWhiteSpace(info.empNum) ? string.Empty : string.Format("{0}人", info.empNum);
                report.status = "";
                report.total_equity = "";
                report.profit_reta = "";
                report.sale_income = info.vendInc;
                report.tax_total = info.ratGro;
                if (info.webSiteInfos != null && info.webSiteInfos.data != null)
                {
                    foreach (var item in info.webSiteInfos.data)
                    {
                        Utility.ClearNullValue<WebsiteItem_Report_GT_JL>(item);
                        WebsiteItem website = new WebsiteItem();
                        website.seq_no = report.websites.Count + 1;
                        website.web_name = item.webSitName;
                        website.web_type = item.webType;
                        website.web_url = item.doMain;
                        report.websites.Add(website);
                    }
                }
                if (info.updataInfos != null && info.updataInfos.data != null)
                {
                    foreach (var item in info.updataInfos.data)
                    {
                        Utility.ClearNullValue<UpdateRecordItem_Report_JL>(item);
                        UpdateRecord updateRecord = new UpdateRecord();
                        updateRecord.seq_no = report.update_records.Count + 1;
                        updateRecord.update_item = item.alitem;
                        updateRecord.before_update = item.altBe;
                        updateRecord.after_update = item.altAf;
                        updateRecord.update_date = item.altDate;
                        report.update_records.Add(updateRecord);
                    }
                }
                if (info.licenceInfos != null && info.licenceInfos.data != null)
                {
                   
                }
                _enterpriseInfo.reports.Add(report);
            }

        }
        #endregion

        #region 解析年报信息--农村合作社
        /// <summary>
        /// 解析年报信息--农村合作社
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseReport_SFC(List<ResponseInfo> responses)
        {
            if (responses != null && responses.Any())
            {
                Report report=new Report();
                foreach (var response in responses)
                {
                    if (response.Name == "reportInfo")
                    {
                        this.LoadAndParseReportBasic_SFC(response.Data, report);
                    }
                    else if (response.Name == "reportWebsiteInfo")
                    {
                        this.LoadAndParseReportWebsite_SFC(response.Data,report);
                    }
                    else if (response.Name == "reportProductionInfo")
                    {
                        this.LoadAndParseReportZCZK_SFC(response.Data,report);
                    }
                    else if (response.Name == "reportAnUpdatesInfo")
                    {
                        this.LoadAndParseReportUpdateRecord_SFC(response.Data, report);
                    }
                }
                if (!string.IsNullOrWhiteSpace(report.report_name) && !string.IsNullOrWhiteSpace(report.report_year))
                {
                    _enterpriseInfo.reports.Add(report);
                }
            }
        }
        #endregion

        #region 解析年报基本信息--农村合作社
        /// <summary>
        /// 解析年报基本信息--农村合作社
        /// </summary>
        /// <param name="response"></param>
        /// <param name="report"></param>
        void LoadAndParseReportBasic_SFC(string response,Report report)
        {
            BsonDocument document = BsonDocument.Parse(response);
            report.report_year = document["ancheYear"].IsBsonNull ? string.Empty : document["ancheYear"].AsString;
            report.report_name = string.Format("{0}年度报告", report.report_year);
            report.report_date = document["ancheDate"].IsBsonNull ? string.Empty : document["ancheDate"].AsString;
            report.reg_no = document["uniscId"].IsBsonNull ? string.Empty : document["uniscId"].AsString;
            report.name = document["entName"].IsBsonNull ? string.Empty : document["entName"].AsString;
            report.email = document["email"].IsBsonNull ? string.Empty : document["email"].AsString;
            report.telephone = document["tel"].IsBsonNull ? string.Empty : document["tel"].AsString;
            report.collegues_num = document["empNum"].IsBsonNull ? string.Empty : document["empNum"].AsString;
            report.if_website = document["hasWebSite"].IsBsonNull ? string.Empty : document["hasWebSite"].AsString;
            
        }

        #endregion

        #region 解析年报网站信息--农村合作社
        void LoadAndParseReportWebsite_SFC(string response, Report report)
        { 
            
        }
        #endregion

        #region 解析年报资产状况信息--农村合作社
        void LoadAndParseReportZCZK_SFC(string response, Report report)
        {
            BsonDocument document = BsonDocument.Parse(response);
            report.sale_income = document["maiBusInc"].IsBsonNull ? string.Empty : document["maiBusInc"].AsString;
            report.tax_total = document["ratGro"].IsBsonNull ? string.Empty : document["ratGro"].AsString;
            report.profit_total = document["priYeaProfit"].IsBsonNull ? string.Empty : document["priYeaProfit"].AsString;
            report.profit_reta = document["priYeaSub"].IsBsonNull ? string.Empty : document["priYeaSub"].AsString;
            report.debit_amount = document["priYeaLoan"].IsBsonNull ? string.Empty : document["priYeaLoan"].AsString;
        }
        #endregion

        #region 解析年报修改信息--农村合作社
        void LoadAndParseReportUpdateRecord_SFC(string response, Report report)
        { 
            
        }
        #endregion
    }
}
