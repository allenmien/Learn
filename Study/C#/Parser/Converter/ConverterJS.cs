using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using HtmlAgilityPack;

using Newtonsoft.Json;
using iOubo.iSpider.Model;
using iOubo.iSpider.Common;
using System.Threading.Tasks;
using System.Configuration;
using System.Text.RegularExpressions;

namespace iOubo.iSpider.Infrastructure.Parser
{
    public class ConverterJS : IConverter
    {
        DataRequest _request;
        RequestInfo _requestInfo;
        RequestXml _requestXml;
        EnterpriseInfo _enterpriseInfo = new EnterpriseInfo();
        List<AbnormalInfo> _abnormals = new List<AbnormalInfo>();
        List<CheckupInfo> _checkups = new List<CheckupInfo>();
        List<string> reportsNeedToLoad = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings.Get("ReportsNeedToLoad"))
            ? new List<string>() : ConfigurationManager.AppSettings.Get("ReportsNeedToLoad").Split(',').ToList();
        string _regNo_EN = string.Empty;
        string _uniScid = string.Empty;
        string _econKind = string.Empty;
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

            //数据请求和解析
            List<ResponseInfo> responseList = _request.GetResponseInfo(_requestXml.GetRequestListByName("gongshang"));
            if (responseList != null && responseList.Any())
            {
                this.LoadAndParseBasic(responseList.First().Data);
                _request.AddOrUpdateRequestParameter("regNo", _regNo_EN);
                _request.AddOrUpdateRequestParameter("uniScid", _uniScid);
                _request.AddOrUpdateRequestParameter("econKind", _econKind);
                responseList = _request.GetResponseInfo(_requestXml.GetRequestListByGroup("basic"));
                this.ParseResponseMainInfo(responseList);
            }
            SummaryEntity summaryEntity = new SummaryEntity()
            {
                Enterprise = _enterpriseInfo,
                Abnormals = _abnormals,
                Checkups = _checkups
            };

            return summaryEntity;
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

        private void ParseResponseMainInfo(List<ResponseInfo> responseList)
        {

            //将Jason Data转成相应的object
            Parallel.ForEach(responseList, new ParallelOptions { MaxDegreeOfParallelism = 1 }, responseInfo => LoadData(responseInfo));
        }
        void LoadData(ResponseInfo responseInfo)
        {
            if (responseInfo == null)
            {
                return;
            }
            if (responseInfo.Name.Equals("partner"))
            {
                this.LoadAndParsePartners(responseInfo.Data);
            }
            else if (responseInfo.Name.Equals("employee"))
            {
                this.LoadAndParseEmployees(responseInfo.Data);
            }
            else if (responseInfo.Name.Equals("branches"))
            {
                this.LoadAndParseBranches(responseInfo.Data);
            }
            else if (responseInfo.Name.Equals("changerecords"))
            {
                this.LoadAndParseChangeRecords(responseInfo.Data);
            }
            else if (responseInfo.Name.Equals("licenses"))
            {
                this.LoadAndParseChangeRecords(responseInfo.Data);
            }
            else if (responseInfo.Name.Equals("changerecords"))
            {
                this.LoadAndParseChangeRecords(responseInfo.Data);
            }
            else if (responseInfo.Name.Equals("licences"))
            {
                this.LoadAndParseLicences(responseInfo.Data);
            }
            else if (responseInfo.Name.Equals("abnormals"))
            {
                this.LoadAndParseAbnormals(responseInfo.Data);
            }
            else if (responseInfo.Name.Equals("checkups"))
            {
                this.LoadAndParseCheckups(responseInfo.Data);
            }
            else if (responseInfo.Name.Equals("mortgages"))
            {
                this.LoadAndParseMortgages(responseInfo.Data);
            }
            else if (responseInfo.Name.Equals("equity_qualities"))
            {
                this.LoadAndParseEquityQualitys(responseInfo.Data);
            }
            else if (responseInfo.Name.Equals("administrative_punishments"))
            {
                this.LoadAndParseAdministrativePunishment(responseInfo.Data);
            }
            else if (responseInfo.Name.Equals("financial_contributions"))
            {
                this.LoadAndParseFinancialContribution(responseInfo.Data);
            }
            else if (responseInfo.Name.Equals("report"))
            {
                this.LoadAndParseReports(responseInfo.Data);
            }
            else if (responseInfo.Name.Equals("judicial_freezes"))
            {
                this.LoadAndParseJudicialFreezes(responseInfo.Data);
            }
            else if (responseInfo.Name.Equals("serious_illegal_tax"))
            {
                this.LoadAndParseMajorTaxViolatioInfo(responseInfo.Data);
            }
            else if (responseInfo.Name.Equals("serious_illegal_executedperson"))
            {
                this.LoadAndParseExecutedPersonInfo(responseInfo.Data);
            }
        }

        #region 解析基本信息--工商公示
        /// <summary>
        /// 解析基本信息--工商公示
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseBasic(string responseData)
        {
            var entity = JsonConvert.DeserializeObject<BasicInfo>(responseData);
            Utility.ClearNullValue<BasicInfo>(entity);
            if (entity != null)
            {
                _regNo_EN = entity.REG_NO_EN;
                _uniScid = entity.UNI_SCID;
                _econKind = entity.ECON_KIND;
                if (entity.REG_NO.Length == 18)
                {
                    _enterpriseInfo.credit_no = entity.REG_NO;
                    _enterpriseInfo.reg_no = entity.REG_NO;
                }
                else
                {
                    _enterpriseInfo.reg_no = entity.REG_NO;
                }
                _enterpriseInfo.name = entity.CORP_NAME.Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                _enterpriseInfo.econ_kind = entity.ZJ_ECON_KIND;
                _enterpriseInfo.oper_name = entity.OPER_MAN_NAME;
                _enterpriseInfo.regist_capi = string.IsNullOrWhiteSpace(entity.CAPI_TYPE_NAME) ? entity.REG_CAPI : entity.REG_CAPI_WS;
                _enterpriseInfo.start_date = entity.START_DATE;
                _enterpriseInfo.term_start = entity.FARE_TERM_START;
                _enterpriseInfo.term_end = entity.FARE_TERM_END;
                _enterpriseInfo.belong_org = entity.BELONG_ORG;
                _enterpriseInfo.check_date = entity.CHECK_DATE;
                _enterpriseInfo.addresses.Add(new Address { name = "注册地址", address = responseData.Contains("yyzz_01.jsp")?entity.FARE_PLACE: entity.ADDR });
                _enterpriseInfo.scope = entity.FARE_SCOPE;
                _enterpriseInfo.status = entity.CORP_STATUS;
                _enterpriseInfo.end_date = string.IsNullOrWhiteSpace(entity.REVOKE_DATE) ? entity.WRITEOFF_DATE : entity.REVOKE_DATE;
            }
        }
        #endregion

        #region 解析股东信息--工商
        /// <summary>
        /// 解析股东信息--工商
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParsePartners(string responseData)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;
            var entity = JsonConvert.DeserializeObject<StockHolderInfo>(responseData);
            Utility.ClearNullValue<StockHolderInfo>(entity);
            
            if (entity != null && entity.data != null && entity.data.Any())
            {
                foreach (var item in entity.data)
                {
                    
                    Partner partner = new Partner();
                    partner.seq_no = item.RN;
                    partner.stock_name = item.STOCK_NAME;
                    partner.stock_type = item.STOCK_TYPE;
                    partner.identify_type =  item.IDENT_TYPE_NAME;
                    partner.identify_no = item.IDENT_NO;

                    var request = this.CreateRequest();
                    request.AddOrUpdateRequestParameter("org", item.ORG);
                    request.AddOrUpdateRequestParameter("id", item.ID);
                    request.AddOrUpdateRequestParameter("seqId", item.SEQ_ID);
                    request.AddOrUpdateRequestParameter("admitMain", item.ADMIT_MAIN);
                    request.AddOrUpdateRequestParameter("capiTypeName", item.CAPI_TYPE_NAME);
                    //request.AddOrUpdateRequestParameter("tmp", DateTime.Now.ToString());
                    var responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("partner"));
                    if (responseList != null && responseList.Any())
                    {
                        if (item.SHOW == "1")
                        {
                            this.LoadAndParsePartner_Datail(responseList, partner);
                        }
                        else
                        {
                            Partner partner_hidden = new Partner();
                            partner_hidden.seq_no = item.RN;
                            partner_hidden.stock_name = item.STOCK_NAME;
                            partner_hidden.stock_type = item.STOCK_TYPE;
                            partner_hidden.identify_type = item.IDENT_TYPE_NAME;
                            partner_hidden.identify_no = item.IDENT_NO;

                            this.LoadAndParsePartner_Datail(responseList, partner_hidden);

                            _enterpriseInfo.partners_hidden.Add(partner_hidden);
                        }
                    }
                   
                    _enterpriseInfo.partners.Add(partner);
                }
            }
        }
        #endregion

        #region 解析股东详情信息--工商
        /// <summary>
        /// 解析股东详情信息--工商
        /// </summary>
        /// <param name="responseList"></param>
        /// <param name="partner"></param>
        void LoadAndParsePartner_Datail(List<ResponseInfo> responseList,Partner partner)
        {
            foreach (var responseInfo in responseList)
            {
                if (string.IsNullOrWhiteSpace(responseInfo.Data)) continue;
                if (responseInfo.Name == "partner_detail")
                {
                    if (!string.IsNullOrEmpty("responseInfo.Data"))
                    {
                        var entity = JsonConvert.DeserializeObject<InvestorDetailInfo>(responseInfo.Data);
                        partner.total_real_capi = entity.REAL_CAPI;
                        partner.total_should_capi = entity.SHOULD_CAPI;
                    }
                }
                else if (responseInfo.Name == "partner_detail_rj")
                {
                    var entity=JsonConvert.DeserializeObject<InvestorDetailRJInfo>(responseInfo.Data);
                    foreach (var item in entity.data)
                    {
                        Utility.ClearNullValue<InvestorDetailRJArrayInfo>(item);
                        ShouldCapiItem sci = new ShouldCapiItem();
                        sci.shoud_capi = item.SHOULD_CAPI;
                        sci.should_capi_date = item.SHOULD_CAPI_DATE;
                        sci.invest_type = item.INVEST_TYPE_NAME;
                        partner.should_capi_items.Add(sci);
                    }
                }
                else if (responseInfo.Name == "partner_detail_sj")
                {
                    var entity = JsonConvert.DeserializeObject<InvestorDetailSJInfo>(responseInfo.Data);
                    foreach (var item in entity.data)
                    {
                        Utility.ClearNullValue<InvestorDetailSJArrayInfo>(item);
                        RealCapiItem rci = new RealCapiItem();
                        rci.real_capi = item.REAL_CAPI;
                        rci.real_capi_date = item.REAL_CAPI_DATE;
                        rci.invest_type = item.INVEST_TYPE_NAME;
                        partner.real_capi_items.Add(rci);
                    }
                }
            }
        }
        #endregion

        #region 解析主要成员信息--工商
        /// <summary>
        /// 解析主要成员信息--工商
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseEmployees(string responseData)
        {
            if (string.IsNullOrWhiteSpace(responseData))
            {
                return;
            }
            var entity = JsonConvert.DeserializeObject<List<StaffItem>>(responseData);
            if (entity != null)
            {
                foreach (StaffItem item in entity)
                {
                    Utility.ClearNullValue<StaffItem>(item);
                    Employee employee = new Employee()
                    {
                        job_title = item.POSITION_NAME,
                        name = item.PERSON_NAME,
                        seq_no = _enterpriseInfo.employees.Count + 1,
                        sex = "",
                        cer_no = ""
                    };
                    _enterpriseInfo.employees.Add(employee);
                }
            }
        }
        #endregion

        #region 解析分支机构--工商
        /// <summary>
        /// 解析分支机构--工商
        /// </summary>
        /// <param name="responseData"></param>
        private void LoadAndParseBranches(string responseData)
        {
            if (string.IsNullOrWhiteSpace(responseData))
            {
                return;
            }
            var branches = JsonConvert.DeserializeObject<List<BranchItem>>(responseData);
           
            if (branches != null)
            {
                foreach (BranchItem item in branches)
                {
                    if (string.IsNullOrWhiteSpace(item.DIST_REG_NO) && string.IsNullOrWhiteSpace(item.DIST_BELONG_ORG))
                    {
                        continue;
                    }
                    Utility.ClearNullValue<BranchItem>(item);
                    Branch branch=new Branch();
                    branch.seq_no = _enterpriseInfo.branches.Count + 1;
                    branch.name = item.DIST_NAME;
                    branch.reg_no = item.DIST_REG_NO;
                    branch.oper_name = item.OPER_MAN_NAME;
                    branch.belong_org = item.DIST_BELONG_ORG;
                    _enterpriseInfo.branches.Add(branch);
                    
                }
            }
        }
        #endregion

        #region 解析变更信息--工商
        /// <summary>
        /// 解析变更信息--工商
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseChangeRecords(string responseData)
        {
            if (string.IsNullOrWhiteSpace(responseData))
            {
                return;
            }
            var changeRecords = JsonConvert.DeserializeObject<ChangeLogInfo>(responseData);
            if (changeRecords != null && changeRecords.data!=null)
            {
                foreach (ChangeLogItem item in changeRecords.data)
                {
                    ChangeRecord changeRecord = new ChangeRecord()
                    {
                        change_item = item.CHANGE_ITEM_NAME,
                        before_content = item.OLD_CONTENT,
                        after_content = item.NEW_CONTENT,
                        change_date = item.CHANGE_DATE,
                        seq_no = item.RN
                    };

                    _enterpriseInfo.changerecords.Add(changeRecord);
                }
            }
           
        }
        #endregion

        #region 解析行政许可信息--工商
        /// <summary>
        /// 解析行政许可信息--工商
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseLicences(string responseData)
        {
            if (string.IsNullOrWhiteSpace(responseData))
            {
                return;
            }
            var info = JsonConvert.DeserializeObject<License>(responseData);
            if (info != null && info.data != null)
            {
                foreach (LicenseItem item in info.data)
                {
                    LicenseInfo lItem = new LicenseInfo();
                    Utility.ClearNullValue<LicenseItem>(item);
                    lItem = new LicenseInfo();
                    lItem.seq_no = item.RN;
                    lItem.number = item.LIC_NO;
                    lItem.name = item.LIC_NAME;
                    lItem.start_date = item.VAL_FROM;
                    lItem.end_date = item.VAL_TO;
                    lItem.department = item.LIC_ORG;
                    lItem.content = item.LICC_ITEM;
                    lItem.status = item.STATUS;
                    _enterpriseInfo.licenses.Add(lItem);
                }
            }
        }
        #endregion

        #region 解析经营异常--工商
        /// <summary>
        /// 解析经营异常--工商
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseAbnormals(string responseData)
        {
            if (string.IsNullOrWhiteSpace(responseData))
            {
                return;
            }
            var info = JsonConvert.DeserializeObject<AbnormalJasonInfo>(responseData);
            if (info != null && info.data != null)
            {
                foreach (AbnormalJasonItem item in info.data)
                {
                    AbnormalInfo dItem = new AbnormalInfo()
                    {
                        name = _enterpriseInfo.name,
                        reg_no = _enterpriseInfo.reg_no,
                        province = _enterpriseInfo.province,
                        in_reason = String.IsNullOrEmpty(item.FACT_REASON) ? "" : item.FACT_REASON,
                        in_date = String.IsNullOrEmpty(item.MARK_DATE) ? "" : item.MARK_DATE,
                        out_reason = String.IsNullOrEmpty(item.REMOVE_REASON) ? "" : item.REMOVE_REASON,
                        out_date = String.IsNullOrEmpty(item.CREATE_DATE) ? "" : item.CREATE_DATE,
                        department = String.IsNullOrEmpty(item.CREATE_ORG) ? "" : item.CREATE_ORG
                    };

                    _abnormals.Add(dItem);
                }
            }
        }
        #endregion

        #region 解析严重违法--重大税收违法案件信息
        void LoadAndParseMajorTaxViolatioInfo(string responseData)
        {
            if (string.IsNullOrWhiteSpace(responseData))
            {
                return;
            }
            var info = JsonConvert.DeserializeObject<MajorTaxViolatioJasonInfo>(responseData);
            if (info != null && info.data != null)
            {

                foreach (MajorTaxViolatioJasonItem item in info.data)
                {
                    GS_MajorTaxViolatioInfo mtv = new GS_MajorTaxViolatioInfo();
                    Utility.ClearNullValue<MajorTaxViolatioJasonItem>(item);
                    mtv.seq_no = _enterpriseInfo.serious_illegal.major_tax_violatio.Count + 1;
                    mtv.character_of_case = item.AJXZ;
                    mtv.major_illegal_facts = item.WFSS;
                    mtv.unit_for_inspection = item.SSJCDW;
                    mtv.detail.taxpayer = item.CORP_NAME;
                    mtv.detail.credit_no = item.REG_NO;
                    mtv.detail.taxpayer_identity_number = item.NSRSBH;
                    mtv.detail.org_no = item.ZZJGDM;
                    mtv.detail.address = item.ZCDZ;
                    mtv.detail.oper_name = item.FDXM;
                    mtv.detail.id_number = string.IsNullOrWhiteSpace(item.FDSFZ) ? "（非公示项）" : item.FDSFZ;
                    mtv.detail.financial_staff = item.CWXM;
                    mtv.detail.fs_sex = item.CWSEX;
                    mtv.detail.fs_id_number = string.IsNullOrWhiteSpace(item.CWSFZ) ? "（非公示项）" : item.CWSFZ; 
                    mtv.detail.character_of_case = item.AJXZ;
                    mtv.detail.handling_penalties = item.YJQC;
                    mtv.detail.unit_for_inspection = item.SSJCDW;
                    mtv.detail.public_date = item.WWGBRQ;
                    _enterpriseInfo.serious_illegal.major_tax_violatio.Add(mtv);
                }
            }
        }
        #endregion

        #region 解析严重违法--失信被执行人信息
        void LoadAndParseExecutedPersonInfo(string responseData)
        {
            if (string.IsNullOrWhiteSpace(responseData))
            {
                return;
            }
            var info = JsonConvert.DeserializeObject<ExecutedPersonJasonInfo>(responseData);
            if (info != null && info.data != null)
            {
                var request = this.CreateRequest();
                foreach (ExecutedPersonJasonItem item in info.data)
                {
                    GS_ExecutedPersonInfo ep = new GS_ExecutedPersonInfo();
                    Utility.ClearNullValue<ExecutedPersonJasonItem>(item);
                    ep.seq_no = _enterpriseInfo.serious_illegal.executed_person.Count + 1;
                    ep.number = item.AH;
                    ep.court = item.ZXFYMC;
                    ep.date = item.LASJ;
                    ep.public_date = item.CREATE_DATE;
                    request.AddOrUpdateRequestParameter("detail_id",item.ID);
                    var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("serious_illegal_executedperson_detail"));
                    if (responseList != null && responseList.Any())
                    {
                        this.LoadAndParseExecutedPersonInfo(responseList.First().Data, ep);
                    }
                    _enterpriseInfo.serious_illegal.executed_person.Add(ep);
                }
            }
        }
        #endregion

        void LoadAndParseExecutedPersonInfo(string responseData, GS_ExecutedPersonInfo ep)
        {
            if (string.IsNullOrWhiteSpace(responseData))
            {
                return;
            }
            var item = JsonConvert.DeserializeObject<ExecutedPersonJasonDetailItem>(responseData);
            if (item != null)
            {
                Utility.ClearNullValue<ExecutedPersonJasonDetailItem>(item);
                ep.detail.number = item.AH;
                ep.detail.date = item.LASJ;
                ep.detail.business = item.ZXYJZW;
                ep.detail.performance = item.LXQKMC;
                ep.detail.al_performance = item.YLXQK;
                ep.detail.un_performance = item.YLXQK;
                ep.detail.court = item.ZXFYMC;
                ep.detail.employer = item.ZXYJZZDW;
                ep.detail.causeofaction = item.ZXAY;
                ep.detail.execution_action = item.SXXWQXMC;
                ep.detail.ws_number = item.CPWS;
                ep.detail.zxyj_number = item.ZXYJWH;
            }
        }

        #region 解析抽查检查--工商
        /// <summary>
        /// 解析抽查检查--工商
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseCheckups(string responseData)
        {
            if (string.IsNullOrWhiteSpace(responseData))
            {
                return;
            }
            var info = JsonConvert.DeserializeObject<CheckupJasonInfo>(responseData);
            if (info != null && info.data != null)
            {
                foreach (CheckupJasonItem item in info.data)
                {
                    CheckupInfo checkup = new CheckupInfo()
                    {
                        name = _enterpriseInfo.name,
                        reg_no = _enterpriseInfo.reg_no,
                        province = _enterpriseInfo.province,
                        department = String.IsNullOrEmpty(item.CHECK_ORG) ? "" : item.CHECK_ORG,
                        type = String.IsNullOrEmpty(item.CHECK_TYPE) ? "" : item.CHECK_TYPE,
                        date = String.IsNullOrEmpty(item.CHECK_DATE) ? "" : item.CHECK_DATE,
                        result = String.IsNullOrEmpty(item.CHECK_RESULT) ? "" : item.CHECK_RESULT
                    };
                    _checkups.Add(checkup);
                }
            }
        }
        #endregion

        #region 解析动产抵押--工商
        /// <summary>
        /// 解析动产抵押--工商
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseMortgages(string responseData) 
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;
            var entity = JsonConvert.DeserializeObject<MortgagesInfo>(responseData);
            Utility.ClearNullValue<MortgagesInfo>(entity);

            if (entity != null && entity.data != null && entity.data.Any())
            {
                foreach (var item in entity.data)
                {
                    MortgageInfo mortgage = new MortgageInfo();
                    mortgage.seq_no = item.RN;
                    mortgage.number = item.GUARANTY_REG_NO;
                    mortgage.date = item.START_DATE;
                    mortgage.department = item.CREATE_ORG;
                    mortgage.amount = item.ASSURE_CAPI;
                    mortgage.status = item.STATUS;
                    mortgage.public_date = item.WRITEOFF_DATE;
                    var request = this.CreateRequest();
                    request.AddOrUpdateRequestParameter("org", item.ORG);
                    request.AddOrUpdateRequestParameter("id", item.ID);
                    request.AddOrUpdateRequestParameter("seqId", item.SEQ_ID);
                    request.AddOrUpdateRequestParameter("tmp", DateTime.Now.ToString());
                    var responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("mortgages"));
                    if (responseList != null && responseList.Any())
                    {
                        foreach (var responseInfo in responseList)
                        {
                            if (responseInfo.Name.Equals("mortgages_dyqrgk"))
                            {
                                var dyqrgk = JsonConvert.DeserializeObject<dyqr>(responseInfo.Data);
                                if (dyqrgk != null && dyqrgk.data!=null)
                                {
                                    foreach (var dyqrgk_item in dyqrgk.data)
                                    {
                                        Mortgagee m = new Mortgagee();
                                        m.seq_no = mortgage.mortgagees.Count + 1;
                                        m.name = dyqrgk_item.AU_NAME;
                                        m.identify_no = dyqrgk_item.AU_CER_NO;
                                        m.identify_type = dyqrgk_item.AU_CER_TYPE;
                                        mortgage.mortgagees.Add(m);
                                    }
                                }
                            }
                            else if (responseInfo.Name.Equals("mortgages_bdbzqgk"))
                            {
                                var bdbzqgk = JsonConvert.DeserializeObject<List<bdbzqgkItem>>(responseInfo.Data);
                                if (bdbzqgk != null && bdbzqgk.Any())
                                {
                                    var first = bdbzqgk.First();
                                    mortgage.debit_type = first.ASSURE_KIND;
                                    mortgage.debit_scope = first.ASSURE_SCOPE;
                                    mortgage.debit_period = first.ASSURE_START_DATE;
                                    mortgage.debit_amount = first.ASSURE_CAPI;
                                    mortgage.debit_remarks = string.IsNullOrWhiteSpace(first.REMARK) ? "" : first.REMARK;
                                }
                            }
                            else if (responseInfo.Name.Equals("mortgages_dywgk"))
                            {
                                var dywgk = JsonConvert.DeserializeObject<dywgk>(responseInfo.Data);
                                if (dywgk != null && dywgk.data != null)
                                {
                                    foreach (var dywgk_item in dywgk.data)
                                    {
                                        Guarantee g = new Guarantee();
                                        g.seq_no = mortgage.guarantees.Count + 1;
                                        g.name = dywgk_item.NAME;
                                        g.belong_to = dywgk_item.BELONG_KIND;
                                        g.desc = string.IsNullOrWhiteSpace(dywgk_item.PA_DETAIL) ? "" : dywgk_item.PA_DETAIL;
                                        g.remarks = string.IsNullOrWhiteSpace(dywgk_item.REMARK) ? "" : dywgk_item.REMARK;
                                        mortgage.guarantees.Add(g);
                                    }
                                }
                            }
                        }
                    }
                    _enterpriseInfo.mortgages.Add(mortgage);
                }
            }
        }
        #endregion
        
        #region 解析股权出质--工商
        /// <summary>
        /// 解析股权出质--工商
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseEquityQualitys(string responseData)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;

            var info = JsonConvert.DeserializeObject<EquityQualityList>(responseData);
            if (info != null && info.data != null)
            {
                foreach (EquityQualityItem item in info.data)
                {
                    Utility.ClearNullValue<EquityQualityItem>(item);

                    if (!string.IsNullOrEmpty(item.D1))
                    {
                        HtmlDocument document = new HtmlDocument();
                        document.LoadHtml(item.D1);
                        HtmlNode rootNode = document.DocumentNode;
                        var tr = rootNode.SelectSingleNode("//tr");
                        if (tr!=null)
                        {
                            var tds = tr.SelectNodes("./td");
                            if (tds != null && tds.Count > 6)
                            {
                                EquityQuality eq = new EquityQuality();
                                eq.seq_no = _enterpriseInfo.equity_qualities.Count + 1;
                                eq.number = tds[1].InnerText;
                                eq.pledgor = tds[2].InnerText;
                                eq.pledgor_identify_no = tds[3].InnerText;
                                eq.pledgor_amount = tds[4].InnerText;
                                eq.pawnee = tds[5].InnerText;
                                eq.pawnee_identify_no = tds[6].InnerText;
                                eq.date = tds[7].InnerText;
                                eq.status = tds[8].InnerText;
                                eq.public_date = tds[9].InnerText;
                                _enterpriseInfo.equity_qualities.Add(eq);
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析行政处罚--工商
        /// <summary>
        /// 解析行政处罚--工商
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseAdministrativePunishment(string responseData)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;

            var info = JsonConvert.DeserializeObject<JS_AdministrativePunishmentInfo>(responseData);
            if (info != null && info.data != null)
            {
                foreach (var item in info.data)
                {
                    Utility.ClearNullValue<JS_AdministrativePunishmentDetailInfo>(item);
                    AdministrativePunishment ap = new AdministrativePunishment();
                    ap.name = _enterpriseInfo.name;
                    ap.oper_name = _enterpriseInfo.oper_name;
                    ap.reg_no = string.IsNullOrWhiteSpace(_enterpriseInfo.reg_no) ? _enterpriseInfo.credit_no : _enterpriseInfo.reg_no;
                    ap.seq_no = item.RN;
                    ap.number = item.PEN_DEC_NO;
                    ap.illegal_type = item.ILLEG_ACT_TYPE;
                    ap.content = item.PEN_TYPE;
                    ap.department = item.PUNISH_ORG_NAME;
                    ap.date = item.PUNISH_DATE;
                    ap.date = item.CREATE_DATE;
                    _enterpriseInfo.administrative_punishments.Add(ap);
                }
            }
        }
        #endregion

        #region 解析股东及出资信息--企业
        /// <summary>
        /// 解析股东及出资信息--企业
        /// </summary>
        /// <param name="responseData"></param>
        private void LoadAndParseFinancialContribution(string responseData)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;
            var info = JsonConvert.DeserializeObject<FinancialContributionList>(responseData);
            HtmlDocument document = new HtmlDocument();
            List<FinancialContribution> fcLst = new List<FinancialContribution>();
            if (info != null && info.data != null)
            {
                foreach (FinancialContributionListItem item in info.data)
                {
                    Utility.ClearNullValue<FinancialContributionListItem>(item);
                    if (item.D1.Trim() == "") continue;
                    document.LoadHtml(item.D1);
                    HtmlNode rootNode = document.DocumentNode;
                    HtmlNodeCollection trList = rootNode.SelectNodes("./tr");

                    if (trList == null || trList.Count() == 0) continue;

                    FinancialContribution fc = new FinancialContribution();

                    foreach (HtmlNode trNode in trList)
                    {
                        HtmlNodeCollection tdList = trNode.SelectNodes("./td");
                        if (tdList != null)
                        {
                            if (tdList.Count == 12)
                            {
                                fc.seq_no = _enterpriseInfo.financial_contributions.Count + 1;
                                fc.investor_name = tdList[1].InnerText;
                                fc.total_should_capi = tdList[2].InnerText;
                                fc.total_real_capi = tdList[3].InnerText;
                                fc.investor_type = "";
                                var sItem = new FinancialContribution.ShouldCapiItem();
                                var rItem = new FinancialContribution.RealCapiItem();
                                sItem.should_invest_type = tdList[4].InnerText;
                                sItem.should_capi = tdList[5].InnerText;
                                sItem.should_invest_date = tdList[6].InnerText;
                                sItem.public_date = tdList[7].InnerText;
                                rItem.real_invest_type = tdList[8].InnerText;
                                rItem.real_capi = tdList[9].InnerText;
                                rItem.real_invest_date = tdList[10].InnerText;
                                rItem.public_date = tdList[11].InnerText;
                                if (!string.IsNullOrWhiteSpace(sItem.should_invest_type) || !string.IsNullOrWhiteSpace(sItem.should_capi) || !string.IsNullOrWhiteSpace(sItem.should_invest_date))
                                {
                                    fc.should_capi_items.Add(sItem);
                                }
                                if (!string.IsNullOrWhiteSpace(rItem.real_invest_type) || !string.IsNullOrWhiteSpace(rItem.real_capi) || !string.IsNullOrWhiteSpace(rItem.real_invest_date))
                                {
                                    fc.real_capi_items.Add(rItem);
                                }
                                
                               
                            }
                            else if (tdList.Count == 8)
                            {
                                var sItem = new FinancialContribution.ShouldCapiItem();
                                var rItem = new FinancialContribution.RealCapiItem();
                                sItem.should_invest_type = tdList[0].InnerText;
                                sItem.should_capi = tdList[1].InnerText;
                                sItem.should_invest_date = tdList[2].InnerText;
                                rItem.real_invest_type = tdList[4].InnerText;
                                rItem.real_capi = tdList[5].InnerText;
                                rItem.real_invest_date = tdList[6].InnerText;
                                if (!string.IsNullOrWhiteSpace(sItem.should_invest_type) || !string.IsNullOrWhiteSpace(sItem.should_capi) || !string.IsNullOrWhiteSpace(sItem.should_invest_date))
                                {
                                    fc.should_capi_items.Add(sItem);
                                }
                                if (!string.IsNullOrWhiteSpace(rItem.real_invest_type) || !string.IsNullOrWhiteSpace(rItem.real_capi) || !string.IsNullOrWhiteSpace(rItem.real_invest_date))
                                {
                                    fc.real_capi_items.Add(rItem);
                                }
                                
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(fc.investor_name))
                    {
                        _enterpriseInfo.financial_contributions.Add(fc);
                    }
                            
                }
            }
            
        }
        #endregion

        #region 解析股权变更信息--企业
        void LoadAndParseStockChanges(string responseData)
        { 
        }
        #endregion

        #region 解析知识产权信息--企业
        /// <summary>
        /// 解析知识产权信息--企业
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseKnowledgeProperties(string responseData)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;

            var info = JsonConvert.DeserializeObject<KnowledgePropertyInfo>(responseData);
            if (info != null && info.data != null)
            {
                foreach (var item in info.data)
                {
                    Utility.ClearNullValue<KnowledgePropertyItem>(item);
                    KnowledgeProperty kp = new KnowledgeProperty();
                    kp.seq_no = item.RN;
                    kp.number = item.TM_REG_NO;
                    kp.name = item.TM_NAME;
                    kp.type = item.TM_KIND;
                    kp.pledgor = item.MORTGAGOR_NAME;
                    kp.pawnee = item.MORTGAGEE_NAME;
                    kp.period = item.START_DATE;
                    kp.status = item.STATUS;
                    kp.public_date = item.CREATE_DATE;
                    _enterpriseInfo.knowledge_properties.Add(kp);
                }
            }
        }
        #endregion

        #region 解析年报信息
        void LoadAndParseReports(string responseData)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;
            var info = JsonConvert.DeserializeObject<ReportInfo_JS>(responseData);
            if (info != null && info.data!=null && info.data.Any())
            {
                foreach (var item in info.data)
                {
                    Report report = new Report();
                    var id = item.ID;
                    var reportName = item.REPORT_YEAR_CN;
                    var reportYear = item.REPORT_YEAR;
                    report.report_year = item.REPORT_YEAR;
                    report.report_name = item.REPORT_YEAR_CN;
                    report.report_date = item.REPORT_DATE;

                    var request = this.CreateRequest();
                    request.AddOrUpdateRequestParameter("id", item.ID);
                    request.AddOrUpdateRequestParameter("reportYear", item.REPORT_YEAR);
                    var responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("report"));
                    if (responseList != null && responseList.Any())
                    {
                        foreach (var responseInfo in responseList)
                        {
                            if (responseInfo.Name == "basicInfo_report")
                            {
                                this.LoadAndParseBasicInfo_Report(responseInfo.Data, report);
                            }
                            else if (responseInfo.Name == "websites_report")
                            {
                                this.LoadAndParseWebsites_Report(responseInfo.Data, report);
                            }
                            else if (responseInfo.Name == "invests_report")
                            {
                                this.LoadAndParseInvests_Report(responseInfo.Data, report);
                            }
                            else if (responseInfo.Name == "partners_report")
                            {
                                this.LoadAndParsePartners_Report(responseInfo.Data, report);
                            }
                            else if (responseInfo.Name == "external_guarantees_report")
                            {
                                this.LoadAndParseExternalGuarantees_Report(responseInfo.Data, report);
                            }
                            else if (responseInfo.Name == "stock_changes_report")
                            {
                                this.LoadAndParseStockChanges_Report(responseInfo.Data, report);
                            }
                            else if (responseInfo.Name == "update_records_report")
                            {
                                this.LoadAndParseUpdateRecords_Report(responseInfo.Data, report);
                            }
                            else if (responseInfo.Name == "shebao_report")
                            {
                                this.LoadAndParseSheBao_Report(responseInfo.Data, report);
                            }
                        }
                    }
                    _enterpriseInfo.reports.Add(report);
                }
            }
        }
        #endregion

        #region 解析基本信息--年报
        /// <summary>
        /// 解析基本信息--年报
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseBasicInfo_Report(string responseData, Report report)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;
            var info = JsonConvert.DeserializeObject<ReportBasicInfo>(responseData);
            if (info != null)
            {
                Utility.ClearNullValue<ReportBasicInfo>(info);
                report.report_year = info.REPORT_YEAR;
                report.report_date = info.REPORT_DATE;
                report.reg_no = info.REG_NO;
                report.name = info.CORP_NAME;
                report.telephone = info.TEL;
                report.zip_code = info.ZIP;
                report.address = info.ADDR;
                report.email = info.E_MAIL;
                report.collegues_num = info.PRAC_PERSON_NUM;
                report.status = info.PRODUCE_STATUS;
                report.if_website = info.IF_WEBSITE;
                report.if_invest = info.IF_INVEST;
                report.if_equity = info.IF_EQUITY;
                report.total_equity = info.NET_AMOUNT;
                report.profit_reta = info.TOTAL_EQUITY;
                report.sale_income = info.SALE_INCOME;
                report.profit_total = info.PROFIT_TOTAL;
                report.serv_fare_income = info.SERV_FARE_INCOME;
                report.net_amount = info.PROFIT_RETA;
                report.tax_total = info.TAX_TOTAL;
                report.debit_amount = info.DEBT_AMOUNT;
            }
        }
        #endregion

        #region 解析网站或网店信息--年报
        /// <summary>
        /// 解析网站或网店信息--年报 
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        void LoadAndParseWebsites_Report(string responseData, Report report)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;
            var list = JsonConvert.DeserializeObject<List<WebsiteJasonItem>>(responseData);
            if (list != null && list.Any())
            {
                foreach (var item in list)
                {
                    WebsiteItem w = new WebsiteItem();
                    w.seq_no = report.websites.Count + 1;
                    w.web_name = item.WEB_NAME;
                    w.web_type = item.WEB_TYPE;
                    w.web_url = item.WEB_URL;
                    report.websites.Add(w);
                }
            }

        }
        #endregion

        #region 解析股东及出资信息--年报
        /// <summary>
        /// 解析股东及出资信息--年报
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParsePartners_Report(string responseData,Report report)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;
            var info = JsonConvert.DeserializeObject<ReportStockInfo>(responseData);
            if (info != null && info.data != null)
            {
                foreach (var item in info.data)
                {
                    Utility.ClearNullValue<ReportStockItem>(item);
                    Partner partner = new Partner()
                    {
                        seq_no = item.RN,
                        stock_name = item.STOCK_NAME,
                        stock_percent = item.STOCK_PERCENT == null ? "" : item.STOCK_PERCENT,
                        stock_type = item.STOCK_TYPE_NAME == null ? "" : item.STOCK_TYPE_NAME,

                    };
                    ShouldCapiItem shouldItem = new ShouldCapiItem()
                    {
                        shoud_capi = item.SHOULD_CAPI,
                        should_capi_date = item.SHOULD_CAPI_DATE,
                        invest_type = item.SHOULD_CAPI_TYPE
                    };
                    RealCapiItem realItem = new RealCapiItem()
                    {
                        real_capi = item.REAL_CAPI,
                        real_capi_date = item.REAL_CAPI_DATE,
                        invest_type = item.REAL_CAPI_TYPE
                    };
                    partner.should_capi_items.Add(shouldItem);
                    partner.real_capi_items.Add(realItem);
                    
                    report.partners.Add(partner);
                }
                
            }
        }
        #endregion

        #region 解析对外投资信息--年报
        /// <summary>
        /// 解析对外投资信息--年报
        /// </summary>
        /// <param name="responeData"></param>
        /// <param name="report"></param>
        void LoadAndParseInvests_Report(string responseData, Report report)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;
            var list = JsonConvert.DeserializeObject<List<InvestJasonItem>>(responseData);
            if (list != null && list.Any())
            {
                foreach (var item in list)
                {
                    Utility.ClearNullValue<InvestJasonItem>(item);
                    InvestItem invest = new InvestItem();
                    invest.seq_no = report.invest_items.Count + 1;
                    invest.invest_name = item.INVEST_NAME;
                    invest.invest_reg_no = item.INVEST_REG_NO;
                    report.invest_items.Add(invest);
                }
            }
        }
        #endregion

        #region 解析提供保证担保信息--年报
        /// <summary>
        /// 解析提供保证担保信息--年报
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        void LoadAndParseExternalGuarantees_Report(string responseData, Report report)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;
            var info = JsonConvert.DeserializeObject<ReportExternalGuaranteesJsonInfo>(responseData);
            if (info != null && info.data != null)
            {
                foreach (var item in info.data)
                {
                    Utility.ClearNullValue<ReportExternalGuaranteesJsonItem>(item);
                    ExternalGuarantee eg = new ExternalGuarantee();
                    eg.seq_no = item.RN;
                    eg.creditor = item.CRED_NAME;
                    eg.debtor = item.DEBT_NAME;
                    eg.type = item.CRED_TYPE;
                    eg.amount = item.CRED_AMOUNT;
                    eg.period = item.GUAR_PERIOD;
                    eg.guarantee_time = item.GUAR_DATE;
                    eg.guarantee_type = item.GUAR_TYPE;
                    report.external_guarantees.Add(eg);
                }
            }
            
        }
        #endregion

        #region 解析社保信息--年报
        void LoadAndParseSheBao_Report(string responseData, Report report)
        {
            if (string.IsNullOrWhiteSpace(responseData) || "{}".Equals(responseData)) return;
            var obj = new
            {
                ID = "",
                ENDOWMENT_NUM = "",
                MEDICARE_NUM = "",
                UNEMPLOYED_NUM = "",
                EMPLOYMENT_INJURY_NUM = "",
                MATERNITY_NUM = "",
                WAGES_JBYL = "",
                WAGES_SYBX = "",
                WAGES_YLBX = "",
                WAGES_SY = "",
                SOCIALINS_JBYL = "",
                SOCIALINS_SYBX = "",
                SOCIALINS_YLBX = "",
                SOCIALINS_GSBX = "",
                SOCIALINS_SY = "",
                PAYMENT_JBYL = "",
                PAYMENT_SYBX = "",
                PAYMENT_YLBX = "",
                PAYMENT_GSBX = "",
                PAYMENT_SY = "",
            };
            var objmous = JsonConvert.DeserializeAnonymousType(responseData, obj);
            if (objmous != null)
            {
                report.social_security.yanglaobx_num = objmous.ENDOWMENT_NUM;
                report.social_security.shiyebx_num = objmous.UNEMPLOYED_NUM;
                report.social_security.yiliaobx_num = objmous.MEDICARE_NUM;
                report.social_security.gongshangbx_num = objmous.EMPLOYMENT_INJURY_NUM;
                report.social_security.shengyubx_num = objmous.MATERNITY_NUM;
                report.social_security.dw_yanglaobx_js = objmous.WAGES_JBYL;
                report.social_security.dw_shiyebx_js = objmous.WAGES_SYBX;
                report.social_security.dw_yiliaobx_js = objmous.WAGES_SY;
                report.social_security.dw_shengyubx_js = objmous.WAGES_SY;
                report.social_security.bq_yanglaobx_je = objmous.SOCIALINS_JBYL;
                report.social_security.bq_shiyebx_je = objmous.SOCIALINS_SYBX;
                report.social_security.bq_yiliaobx_je = objmous.SOCIALINS_YLBX;
                report.social_security.bq_gongshangbx_je = objmous.SOCIALINS_GSBX;
                report.social_security.bq_shengyubx_je = objmous.SOCIALINS_SY;
                report.social_security.dw_yanglaobx_je = objmous.PAYMENT_JBYL;
                report.social_security.dw_shiyebx_je = objmous.PAYMENT_SYBX;
                report.social_security.dw_yiliaobx_je = objmous.PAYMENT_YLBX;
                report.social_security.dw_gongshangbx_je = objmous.PAYMENT_GSBX;
                report.social_security.dw_shengyubx_je = objmous.PAYMENT_SY;
            }
        }
        #endregion

        #region 解析股权变更--年报
        /// <summary>
        /// 解析股权变更--年报
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="?"></param>
        void LoadAndParseStockChanges_Report(string responseData,Report report)
        { 
           if (string.IsNullOrWhiteSpace(responseData)) return;
            var info = JsonConvert.DeserializeObject<ReportStockChangeJsonInfo>(responseData);
            if (info != null && info.data != null)
            {
                foreach (var item in info.data)
                {
                    Utility.ClearNullValue<ReportStockChangeJsonItem>(item);
                    var sc = new StockChangeItem();
                    sc.seq_no = report.stock_changes.Count + 1;
                    sc.name = item.STOCK_NAME;
                    sc.change_date = item.CHANGE_DATE;
                    sc.before_percent = item.CHANGE_BEFORE;
                    sc.after_percent = item.CHANGE_AFTER;
                    report.stock_changes.Add(sc);
                }
            } 
        }
        #endregion

        #region 解析修改信息--年报
        /// <summary>
        /// 解析修改信息--年报
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        void LoadAndParseUpdateRecords_Report(string responseData, Report report)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;
            var info = JsonConvert.DeserializeObject<ReportUpdateRecordInfo>(responseData);
            if (info != null && info.data != null)
            {
                foreach (var item in info.data)
                {
                    UpdateRecord ur = new UpdateRecord();
                    ur.seq_no = item.RN;
                    ur.update_item = item.CHANGE_ITEM_NAME;
                    ur.update_date = item.CHANGE_DATE;
                    ur.before_update = item.OLD_CONTENT;
                    ur.after_update = item.NEW_CONTENT;

                    report.update_records.Add(ur);
                }
            }
        }
        #endregion

        #region 解析股权冻结信息--司法协助
        /// <summary>
        /// 解析股权冻结信息--司法协助
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseJudicialFreezes(string responseData)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;
            var request = this.CreateRequest();
            var info = JsonConvert.DeserializeObject<JudicialFreezeList>(responseData);
            if (info != null && info.data != null && info.data.Any())
            {
                foreach(var item in info.data)
                {
                    JudicialFreeze jf = new JudicialFreeze();
                    jf.seq_no = item.RN;
                    jf.be_executed_person = item.ASSIST_NAME;
                    jf.amount = item.FREEZE_AMOUNT;
                    jf.executive_court = item.EXECUTE_COURT;
                    jf.number = item.NOTICE_NO;
                    jf.status = item.FREEZE_STATUS;
                    jf.type = "股权冻结";
                    request.AddOrUpdateRequestParameter("id",item.ID);
                    request.AddOrUpdateRequestParameter("org", item.ORG);
                    request.AddOrUpdateRequestParameter("tmp", DateTime.Now.ToString());
                    if (jf.status == "股权变更" || jf.status == "股东变更")
                    {
                        var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("judicial_freezes_detail_stockchange"));
                        if (responseList != null && responseList.Any())
                        {
                            var first = responseList.First();
                            jf.type = "股权变更";
                            this.LoadAndParseJudicialFreezeDetail_StockChange(first.Data, jf);
                        }
                        
                    }
                    else
                    {
                        var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("judicial_freezes_detail"));
                        if (responseList != null && responseList.Any())
                        {
                            var first = responseList.First();
                            this.LoadAndParseJudicialFreezeDetail(first.Data, jf);
                        }
                    }
                    
                    _enterpriseInfo.judicial_freezes.Add(jf);
                }
                
            }
        }
        #endregion

        #region 解析股权冻结详情信息--司法协助
        /// <summary>
        /// 解析股权冻结详情信息--司法协助
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="jf"></param>
        void LoadAndParseJudicialFreezeDetail(string responseData, JudicialFreeze jf)
        {
            var info = JsonConvert.DeserializeObject<JudicialFreezeDetailList>(responseData);
            if (info != null)
            {
                if (info.djInfo != null && info.djInfo.Any())
                { 
                    var first=info.djInfo.First();
                    jf.detail.adjudicate_no = first.ADJUDICATE_NO;
                    jf.detail.assist_ident_no = first.ASSIST_IDENT_NO;
                    jf.detail.assist_ident_type = first.ASSIST_IDENT_TYPE;
                    jf.detail.corp_name = first.CORP_NAME;
                    jf.detail.assist_name = first.ASSIST_NAME;
                    jf.detail.assist_item = first.ASSIST_ITEM;
                    jf.detail.execute_court = first.EXECUTE_COURT;
                    jf.detail.freeze_amount = first.FREEZE_AMOUNT;
                    jf.detail.freeze_end_date = first.FREEZE_END_DATE;
                    jf.detail.freeze_start_date = first.FREEZE_START_DATE;
                    jf.detail.freeze_year_month = first.FREEZE_YEAR_MONTH;
                    jf.detail.notice_no = first.NOTICE_NO;
                    jf.detail.public_date = first.PUBLIC_DATE;
                    
                }
                if(info.jdInfo!=null && info.jdInfo.Any())
                {
                    var first = info.jdInfo.First();
                    JudicialUnFreezeDetail unFreezeDetail = new JudicialUnFreezeDetail();
                    unFreezeDetail.adjudicate_no = first.ADJUDICATE_NO;
                    unFreezeDetail.assist_ident_no = first.ASSIST_IDENT_NO;
                    unFreezeDetail.assist_ident_type = first.ASSIST_IDENT_TYPE;
                    unFreezeDetail.corp_name = "";
                    unFreezeDetail.assist_name = first.ASSIST_NAME;
                    unFreezeDetail.assist_item = first.ASSIST_ITEM;
                    unFreezeDetail.execute_court = first.EXECUTE_COURT;
                    unFreezeDetail.freeze_amount = first.FREEZE_AMOUNT;
                    unFreezeDetail.unfreeze_date = first.REMOVE_DATE;
                    unFreezeDetail.notice_no = first.NOTICE_NO;
                    unFreezeDetail.public_date = first.PUBLIC_DATE;
                    jf.un_freeze_detail = unFreezeDetail;
                    jf.un_freeze_details.Add(unFreezeDetail);
                }
            }
        }
        #endregion

        #region 解析股权变更--司法协助
        /// <summary>
        /// 解析股权变更--司法协助
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="jf"></param>
        void LoadAndParseJudicialFreezeDetail_StockChange(string responseData, JudicialFreeze jf)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;
            var obj = new[] { 
                new { 
                    CORP_NAME="",
                    EXECUTE_COURT="",
                    ASSIST_ITEM="",
                    ADJUDICATE_NO="",
                    NOTICE_NO="",
                    ASSIST_NAME="",
                    CHANGE_AMOUNT="",
                    ASSIST_IDENT_TYPE="",
                    ASSIST_IDENT_NO="",
                    ACCEPT_NAME="",
                    ASSIST_DATE="",
                    ACCEPT_IDENT_TYPE="",
                    ACCEPT_IDENT_NO=""
                } 
            };
            var arr = JsonConvert.DeserializeAnonymousType(responseData, obj);
            if (arr != null && arr.Any())
            {
                var first = arr.First();
                jf.pc_freeze_detail.execute_court = first.EXECUTE_COURT;
                jf.pc_freeze_detail.assist_item = first.ASSIST_ITEM;
                jf.pc_freeze_detail.adjudicate_no = first.ADJUDICATE_NO;
                jf.pc_freeze_detail.notice_no = first.NOTICE_NO;
                jf.pc_freeze_detail.assist_name = first.ASSIST_NAME;
                jf.pc_freeze_detail.freeze_amount = first.CHANGE_AMOUNT;
                jf.pc_freeze_detail.assist_ident_type = first.ASSIST_IDENT_TYPE;
                jf.pc_freeze_detail.assist_ident_no = first.ASSIST_IDENT_NO;
                jf.pc_freeze_detail.assignee = first.ACCEPT_NAME;
                jf.pc_freeze_detail.xz_execute_date = first.ASSIST_DATE;
                jf.pc_freeze_detail.assignee_ident_type = first.ACCEPT_IDENT_TYPE;
                jf.pc_freeze_detail.assignee_ident_no = first.ACCEPT_IDENT_NO;

            }
        }
        #endregion

        
    }
}
