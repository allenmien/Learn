using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Collections;
using System.Xml.Linq;
using System.Text.RegularExpressions;

using Newtonsoft.Json;
using iOubo.iSpider.Model;
using HtmlAgilityPack;
using iOubo.iSpider.Common;
using System.Configuration;
using MongoDB.Bson;
using System.Web.Script.Serialization;

namespace iOubo.iSpider.Infrastructure.Parser
{
    public class ConverterCQ : IConverter
    {
        DataRequest _request;
        RequestInfo _requestInfo;
        RequestXml _requestXml;
        EnterpriseInfo _enterpriseInfo = new EnterpriseInfo();
        List<AbnormalInfo> _abnormals = new List<AbnormalInfo>();
        List<CheckupInfo> _checkups = new List<CheckupInfo>();
        const string NoPublish = "企业选择不公示";

        public RequestHandler request = new RequestHandler();

        List<string> reportsNeedToLoad = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings.Get("ReportsNeedToLoad"))
? new List<string>() : ConfigurationManager.AppSettings.Get("ReportsNeedToLoad").Split(',').ToList();
        int _parallelCount = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings.Get("ParallelCount")) ? 10 : int.Parse(ConfigurationManager.AppSettings.Get("ParallelCount"));
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

            //解析基本信息
            List<XElement> requestList = null;
            requestList = _requestXml.GetRequestListByGroup("qy").ToList();
            if (requestInfo.Parameters["pritype"] == "5")
            {
                requestList = _requestXml.GetRequestListByGroup("gt").ToList();
            }
            else
            {
                requestList = _requestXml.GetRequestListByGroup("qy").ToList();
            }
            List<ResponseInfo> responseList = _request.GetResponseInfo(requestList);

            InitialEnterpriseInfo();
            //foreach (ResponseInfo response in responseList)
            //{
            //    LoadBasicInfo(response);
            //}
            Parallel.ForEach(responseList, new ParallelOptions { MaxDegreeOfParallelism = _parallelCount }, item => this.LoadBasicInfo(item));
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

        #region LoadBasicInfo
        void LoadBasicInfo(ResponseInfo item)
        {
            switch (item.Name)
            {
                case "basic":
                    LoadAndParseBasicInfo(item.Data);
                    break;
                case "gtbasic":
                    LoadAndParseGtBasicInfo(item.Data);
                    break;
                case "invest":
                    LoadAndParsePartners(item.Data);
                    break;
                case "employee":
                    LoadAndParseStaffs(item.Data);
                    break;
                case "alter":
                    LoadAndParseChangeRecords(item.Data);
                    break;
                case "branch":
                    LoadAndParseBranches(item.Data);
                    break;
                case "motage":
                    LoadAndParseMortgageInfoItems(item.Data);
                    break;
                case "eotpermit":
                   LoadAndParseLicenseInfo(item.Data);
                    break;
                case "eiminvsralt":
                    LoadAndParseStockChanges(item.Data);
                    break;
                case "abnormal":
                    LoadAndParseAbnormalItems(item.Data);
                    break;
                case "check":
                    LoadAndParseCheckUpItems(item.Data);
                    break;
                case "report":
                    LoadAndParseReports(item.Data);
                    break;
                case "gqcz":
                    LoadAndParseEquityQualityItems(item.Data);
                    break;
                case "financial":
                    LoadAndParseFinancialContribution(item.Data);
                    break;
                case "judicial_freeze":
                    this.LoadAndParseJudicialFreeze(item.Data);
                    break;
            }
        }
        #endregion

        #region GetResponseInfo
        private List<ResponseInfo> GetResponseInfo(IEnumerable<XElement> elements)
        {
            List<ResponseInfo> responseList = new List<ResponseInfo>();
            foreach (XElement el in elements)
            {
                responseList.Add(this._request.RequestData(el));
            }

            return responseList;
        }
        #endregion

        #region 解析个体基本信息
        /// <summary>
        /// 解析个体基本信息
        /// </summary>
        /// <param name="response"></param>
        private void LoadAndParseGtBasicInfo(string response)
        {
            response = response.Replace("\\", "").Replace("[{", "{").Replace("}]", "}");
            CQGtBaseList cqInfos = JsonConvert.DeserializeObject<CQGtBaseList>(response.Replace("\\", ""));
            CQGtBaseInfo cqInfo = cqInfos.form;
            Utility.ClearNullValue<CQGtBaseInfo>(cqInfo);
            if (cqInfo.uniscid != null && cqInfo.uniscid.Length == 18)
            {
                _enterpriseInfo.credit_no = cqInfo.uniscid;
            }
            else if (cqInfo.uniscid != null && cqInfo.uniscid.Length < 18)
            {
                _enterpriseInfo.reg_no = cqInfo.uniscid;
            }
            _enterpriseInfo.name = cqInfo.traname;
            _enterpriseInfo.addresses.Add(new Address("注册地址", cqInfo.oploc, ""));
            _enterpriseInfo.belong_org = cqInfo.regorg_cn;
            _enterpriseInfo.check_date = cqInfo.apprdate.Split(' ')[0];
            _enterpriseInfo.econ_kind = cqInfo.enttype_cn;
            _enterpriseInfo.oper_name = string.IsNullOrEmpty(cqInfo.name) ? "" : cqInfo.name;            
            _enterpriseInfo.scope = string.IsNullOrEmpty(cqInfo.opscope) ? string.Empty : cqInfo.opscope;
            _enterpriseInfo.start_date = string.IsNullOrEmpty(cqInfo.estdate) ? string.Empty : cqInfo.estdate.Split(' ')[0];
            _enterpriseInfo.status = cqInfo.regstate_cn;
            _enterpriseInfo.econ_kind = cqInfo.enttype_cn;
            _enterpriseInfo.type_desc = cqInfo.compform_cn;
        }
        #endregion

        #region 解析股权冻结信息
        /// <summary>
        /// 解析股权冻结信息
        /// </summary>
        /// <param name="response"></param>
        void LoadAndParseJudicialFreeze(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return;
            response=response.Trim(new char[]{'[',']'});
            BsonDocument document = BsonDocument.Parse(response);
            
            if (document != null && document.Contains("list") && document["list"].IsBsonArray && document["list"].AsBsonArray.Any())
            {
                var list = document["list"].AsBsonArray;
                foreach (BsonDocument item in list)
                {
                    JudicialFreeze jf = new JudicialFreeze();
                    jf.seq_no = _enterpriseInfo.judicial_freezes.Count + 1;
                    jf.be_executed_person = item.Contains("inv") ? (item["inv"].IsBsonNull ? string.Empty : item["inv"].AsString) : string.Empty;
                    jf.amount = item.Contains("froam") ? (item["froam"].IsBsonNull ? string.Empty : item["froam"].AsString) : string.Empty;
                    jf.executive_court = item.Contains("froauth") ? (item["froauth"].IsBsonNull ? string.Empty : item["froauth"].AsString) : string.Empty;
                    jf.number = item.Contains("executeno") ? (item["executeno"].IsBsonNull ? string.Empty : item["executeno"].AsString) : string.Empty;
                    jf.status = item.Contains("frozstate_cn") ? (item["frozstate_cn"].IsBsonNull ? string.Empty : item["frozstate_cn"].AsString) : string.Empty;

                    string url = string.Format("http://cq.gsxt.gov.cn/gsxt/api/esfinfo/queryGqdjFrom/{0}?currentpage=1&pagesize=5&t=1486452345133", item["parent_id"].AsString);
                    var str = request.HttpGet(url, "", "CQ");
                    if (!string.IsNullOrWhiteSpace(str))
                    {
                        this.LoadAndParseJudicialFreezeDetail(str,jf);
                    }
                    _enterpriseInfo.judicial_freezes.Add(jf);
                }
            }
        }

        #endregion

        #region 解析股权冻结详情信息
        void LoadAndParseJudicialFreezeDetail(string response,JudicialFreeze jf)
        {
            response = response.Trim(new char[] { '[', ']' });
            BsonDocument document = BsonDocument.Parse(response);

            if (document != null && document.Contains("form") && !document["form"].IsBsonNull)
            {
                BsonDocument item = document["form"].AsBsonDocument;
                jf.detail.execute_court = item.Contains("froauth") ? (item["froauth"].IsBsonNull ? string.Empty : item["froauth"].AsString) : string.Empty;
                jf.detail.assist_item = item.Contains("executeitem_cn") ? (item["executeitem_cn"].IsBsonNull ? string.Empty : item["executeitem_cn"].AsString) : string.Empty;
                jf.detail.adjudicate_no = item.Contains("executeno") ? (item["executeno"].IsBsonNull ? string.Empty : item["executeno"].AsString) : string.Empty;
                jf.detail.notice_no = item.Contains("frodocno") ? (item["frodocno"].IsBsonNull ? string.Empty : item["frodocno"].AsString) : string.Empty;
                jf.detail.assist_name = item.Contains("inv") ? (item["inv"].IsBsonNull ? string.Empty : item["inv"].AsString) : string.Empty;
                jf.detail.freeze_amount = item.Contains("froam") ? (item["froam"].IsBsonNull ? string.Empty : item["froam"].AsString) : string.Empty;
                jf.detail.freeze_amount = string.IsNullOrWhiteSpace(jf.detail.freeze_amount) ? string.Empty : jf.detail.freeze_amount + "万" + item["regcapcur_cn"].AsString;
                jf.detail.assist_ident_type = item.Contains("blictype_cn") ? (item["blictype_cn"].IsBsonNull ? string.Empty : item["blictype_cn"].AsString) : string.Empty;
                jf.detail.assist_ident_no = item.Contains("blicno") ? (item["blicno"].IsBsonNull ? string.Empty : item["blicno"].AsString) : string.Empty;
                jf.detail.freeze_start_date = item.Contains("frofrom") ? (item["frofrom"].IsBsonNull ? string.Empty : item["frofrom"].AsString) : string.Empty;
                jf.detail.freeze_start_date = this.ConvertStringToDate(jf.detail.freeze_start_date);
                jf.detail.freeze_end_date = item.Contains("froto") ? (item["froto"].IsBsonNull ? string.Empty : item["froto"].AsString) : string.Empty;
                jf.detail.freeze_end_date = this.ConvertStringToDate(jf.detail.freeze_end_date);
                jf.detail.freeze_year_month = item.Contains("frozdeadline") ? (item["frozdeadline"].IsBsonNull ? string.Empty : item["frozdeadline"].AsString) : string.Empty;
                jf.detail.freeze_year_month = string.IsNullOrWhiteSpace(jf.detail.freeze_year_month) ? string.Empty : jf.detail.freeze_year_month + "天";
                jf.detail.public_date = item.Contains("publicdate") ? (item["publicdate"].IsBsonNull ? string.Empty : item["publicdate"].AsString) : string.Empty;
                jf.detail.public_date = this.ConvertStringToDate(jf.detail.public_date);
            }
        }
        #endregion

        #region 日期转化
        string ConvertStringToDate(string str)
        {
            DateTime dt;
            if (string.IsNullOrWhiteSpace(str)) return string.Empty;
            if (DateTime.TryParse(str, out dt))
            {
                return dt.ToString("yyyy年MM月dd日");
            }
            else
            {
                return string.Empty;
            }
        }
        #endregion

        #region 解析基本信息
        /// <summary>
        /// 解析基本信息
        /// </summary>       
        /// <param name="cqInfo"></param>
        private void LoadAndParseBasicInfo(string response)
        {
            response = response.Replace("\\", "").Replace("[{", "{").Replace("}]", "}");
            CQBase cqInfos = JsonConvert.DeserializeObject<CQBase>(response.Replace("\\",""));
            CQBaseInfo cqInfo = cqInfos.form;
            Utility.ClearNullValue<CQBaseInfo>(cqInfo);
            if (cqInfo.uniscid != null && cqInfo.uniscid.Length == 18)
            {
                _enterpriseInfo.credit_no = cqInfo.uniscid;
            }
            else if (cqInfo.uniscid != null && cqInfo.uniscid.Length < 18)
            {
                _enterpriseInfo.reg_no = cqInfo.uniscid;
            }
            _enterpriseInfo.name = cqInfo.entname;
            _enterpriseInfo.addresses.Add(new Address("注册地址", cqInfo.dom, ""));
            _enterpriseInfo.belong_org = cqInfo.regorg_cn;
            _enterpriseInfo.check_date = cqInfo.apprdate.Split(' ')[0];
            _enterpriseInfo.econ_kind = cqInfo.enttype_cn;
            _enterpriseInfo.oper_name = string.IsNullOrEmpty(cqInfo.name) ? "" : cqInfo.name;
            _enterpriseInfo.regist_capi = string.IsNullOrEmpty(cqInfo.regcap) ? string.IsNullOrEmpty(cqInfo.fundam) ? "" : cqInfo.fundam : cqInfo.regcap
                + (string.IsNullOrWhiteSpace(cqInfo.regcapcur_cn) ? "万元" : "万" + cqInfo.regcapcur_cn);
            _enterpriseInfo.scope = string.IsNullOrEmpty(cqInfo.opscope) ? string.Empty : cqInfo.opscope;
            _enterpriseInfo.start_date = string.IsNullOrEmpty(cqInfo.estdate) ? string.Empty : cqInfo.estdate.Split(' ')[0];
            _enterpriseInfo.status = cqInfo.regstate_cn;
            _enterpriseInfo.term_start = cqInfo.opfrom.Split(' ')[0];
            _enterpriseInfo.term_end = string.IsNullOrEmpty(cqInfo.opto) ? "永久" : cqInfo.opto;
        }
        #endregion

        #region 股权变更
        private void LoadAndParseStockChanges(String  response)
        {
            //股权变更
            response = response.Replace("[{\"total", "{\"total").Replace("]}]", "]}");
            CQStockChangeList cqInfo = JsonConvert.DeserializeObject<CQStockChangeList>(response);
            
            List<StockChangeItem> lst = new List<StockChangeItem>();
            if (cqInfo != null && cqInfo.list.Length >0)
            {
                for (int i = 0; i < cqInfo.list.Length; i++)
                {
                    CQStockChangeInfo cq = cqInfo.list[i];
                    Utility.ClearNullValue<CQStockChangeInfo>(cq);
                    StockChangeItem item = new StockChangeItem();
                    item.seq_no = lst.Count + 1;
                    item.name = cq.inv;
                    item.before_percent = string.IsNullOrWhiteSpace(cq.transamprbf) ? "" : cq.transamprbf + "%";
                    item.after_percent = string.IsNullOrWhiteSpace(cq.transampraf) ? "" : cq.transampraf + "%";
                    item.change_date = string.IsNullOrEmpty(cq.altdate) ? "" : cq.altdate.Split(' ')[0];
                    lst.Add(item);
                }
            }
            _enterpriseInfo.stock_changes = lst;
        }
        #endregion

        #region 解析股东信息
        /// <summary>
        /// 解析股东信息
        /// </summary>        
        /// <param name="cqInfo"></param>
        private void LoadAndParsePartners(string response)
        {
            response = response.Replace("[{\"total", "{\"total").Replace("]}]", "]}");
            CQgd cqInfo = JsonConvert.DeserializeObject<CQgd>(response);
            List<Partner> partnerList = new List<Partner>();
            if (cqInfo.list != null && cqInfo.list.Length > 0)
            {
                for (int i = 0; i < cqInfo.list.Length; i++)
                {

                    Investor item = cqInfo.list[i];
                    Partner partner = new Partner();
                    Utility.ClearNullValue<Investor>(item);
                    partner.identify_no = string.IsNullOrEmpty(item.blicno) ? "" : item.blicno;
                    partner.identify_type = string.IsNullOrEmpty(item.blictype_cn) ? "" : item.blictype_cn;
                    partner.stock_type = string.IsNullOrEmpty(item.invtype_cn) ? "" : item.invtype_cn;
                    partner.stock_name = item.inv;
                    partner.seq_no = partnerList.Count + 1;
                    partner.stock_percent = "";
                    partner.should_capi_items = new List<ShouldCapiItem>();
                    partner.real_capi_items = new List<RealCapiItem>();
                    string url = string.Format("http://cq.gsxt.gov.cn/gsxt/api/einv/gdxx/{0}?currentpage=1&pagesize=100&t=1482748722399", item.invid);
                   
                    var result = request.HttpGet(url, string.Empty, "CQ");
                    
                   JavaScriptSerializer jsonSerializer = new JavaScriptSerializer();
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        var json = jsonSerializer.Deserialize<CQDetailGd>(result.Replace("[{\"form\":", "").Replace("}]", ""));
                        partner.total_should_capi = json.lisubconam;
                        partner.total_real_capi = json.liacconam;
                    }
                    // var real_result = request.HttpGet(real_url, string.Empty, "CQ");
                    string should_url = string.Format("http://cq.gsxt.gov.cn/gsxt/api/einvpaidin/queryList/{0}?currentpage=1&pagesize=100&t=1487661365148", item.invid);
                    result = request.HttpGet(should_url, string.Empty, "CQ");
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        CQPartnerShould json = jsonSerializer.Deserialize<CQPartnerShould>(result.TrimStart('[').TrimEnd(']'));
                        if (json.list != null)
                        {
                            foreach (var it in json.list)
                            {
                                ShouldCapiItem shouldItem = new ShouldCapiItem();
                                shouldItem.shoud_capi = it.subconam;
                                shouldItem.should_capi_date = it.condate;
                                shouldItem.invest_type = it.conform_cn;
                                partner.should_capi_items.Add(shouldItem);
                            }
                        }
                    }
                    string real_url = string.Format("http://cq.gsxt.gov.cn/gsxt/api/efactcontribution/queryList/{0}?currentpage=1&pagesize=5&t=1487661365159", item.invid);
                    result = request.HttpGet(real_url, string.Empty, "CQ");
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        CQGdczdetail json = jsonSerializer.Deserialize<CQGdczdetail>(result.TrimStart('[').TrimEnd(']'));
                        if (json.list != null)
                        {
                            foreach (var it in json.list)
                            {
                                RealCapiItem realItem = new RealCapiItem();
                                realItem.real_capi = it.acconam;
                                realItem.real_capi_date = it.condate;
                                realItem.invest_type = it.conform_cn;
                                partner.real_capi_items.Add(realItem);
                            }
                        }
                    }
                    partnerList.Add(partner);

                }
            }
            _enterpriseInfo.partners = partnerList;
        }
        #endregion

        #region 解析主要人员

        /// <summary>
        /// 解析主要人员
        /// </summary>
        /// <param name="cqInfo"></param>
        private void LoadAndParseStaffs(string response)
        {
            response = response.Replace("[{\"list", "{\"list").Replace("[[", "[").Replace("]]", "]").Replace("]}]", "]}").Replace("}],", "},").Replace("，", ",").Replace(",[{", ",{");
            CQMemInfo cqInfos = JsonConvert.DeserializeObject<CQMemInfo>(response);
            CQMember[] cqInfo = cqInfos.list;
            List<Employee> employeeList = new List<Employee>();
            if (cqInfo != null && cqInfo.Length > 0)
            {
                for (int i = 0; i < cqInfo.Length; i++)
                {
                    CQMember item = cqInfo[i];
                    Utility.ClearNullValue<CQMember>(item);
                    Employee employee1 = new Employee();
                    employee1.job_title = item.position_cn.Trim();
                    employee1.name = item.name;
                    employee1.seq_no = i + 1;
                    employee1.sex = "";
                    employee1.cer_no = "";
                    employeeList.Add(employee1);
                }
            }
            _enterpriseInfo.employees = employeeList;
        }
        #endregion

        #region 解析分支机构
        /// <summary>
        /// 解析分支机构
        /// </summary>
        /// <param name="cqInfo"></param>
        private void LoadAndParseBranches(string response)
        {
            //response = response.Replace("[[", "[").Replace("]]", "]").Replace("[{\"list", "{\"list").Replace("]}]", "]}"); 
            response = response.Trim(new char[] { '[', ']' }).Replace("[[", "[").Replace("]]", "]").Replace("],[", ",");
            CQBrunchInfo cqInfos = JsonConvert.DeserializeObject<CQBrunchInfo>(response);
            CQBrunch[] cqInfo = cqInfos.list;
            List<Branch> branchList = new List<Branch>();
            if (cqInfo != null && cqInfo.Length > 0)
            {
                for (int i = 0; i < cqInfo.Length; i++)
                {
                    CQBrunch item = cqInfo[i];
                    Utility.ClearNullValue<CQBrunch>(item);
                    Branch branch = new Branch();
                    branch.belong_org = item.regorg_cn;
                    branch.name = item.brname;
                    branch.seq_no = i + 1;
                    branch.oper_name = "";
                    branch.reg_no = item.uniscid;
                    branchList.Add(branch);
                }
            }
            _enterpriseInfo.branches = branchList;
        }
        #endregion

        #region 解析变更
        /// <summary>
        /// 解析变更
        /// </summary>
        /// <param name="cqInfo"></param>
        private void LoadAndParseChangeRecords(string response)
        {
            response = response.Replace("[{\"total", "{\"total").Replace("]}]", "]}");
            CQBgxx cqInfo = JsonConvert.DeserializeObject<CQBgxx>(response);
            List<ChangeRecord> changeRecordList = new List<ChangeRecord>();
            if (cqInfo.list != null && cqInfo.list.Length > 0)
            {
                for (int i = 0; i < cqInfo.list.Length; i++)
                {
                    Alter item = cqInfo.list[i];
                    Utility.ClearNullValue<Alter>(item);
                    ChangeRecord changeRecord = new ChangeRecord();
                    changeRecord.change_item = item.altitem_cn;
                    changeRecord.before_content = item.altbe;
                    changeRecord.after_content = item.altaf;
                    changeRecord.change_date = item.altdate.Split(' ')[0];
                    changeRecord.seq_no = i + 1;
                    changeRecordList.Add(changeRecord);
                }
            }
            _enterpriseInfo.changerecords = changeRecordList;
        }
        #endregion

        #region 解析经营异常信息
        /// <summary>
        /// 解析经营异常信息
        /// </summary>
        /// <param name="cqInfo"></param>
        private void LoadAndParseAbnormalItems(String response)
        {
            response = response.Replace("[{\"total", "{\"total").Replace("]}]", "]}");
            CQAbnormalList cqInfo = JsonConvert.DeserializeObject<CQAbnormalList>(response);
            List<AbnormalInfo> list = new List<AbnormalInfo>();
            if (cqInfo.list != null && cqInfo.list.Length > 0)
            {
                for (int i = 0; i < cqInfo.list.Length; i++)
                {
                    CQAbnormalInfo item = cqInfo.list[i];
                    Utility.ClearNullValue<CQAbnormalInfo>(item);
                    AbnormalInfo dItem = new AbnormalInfo();
                    dItem.name = _enterpriseInfo.name;
                    dItem.reg_no = _enterpriseInfo.reg_no;
                    dItem.province = _enterpriseInfo.province;
                    dItem.in_reason = item.specause_cn;
                    dItem.in_date = string.IsNullOrEmpty(item.abntime) ? "" : item.abntime.Split(' ')[0];
                    dItem.out_reason = item.remexcpres_cn;
                    dItem.out_date = item.remdate;
                    dItem.department = item.decorg_cn;

                    list.Add(dItem);
                }
            }

            _abnormals = list;
        }
        #endregion

        #region 抽查检查
        /// <summary>
        /// 抽查检查
        /// </summary>
        /// <param name="cqInfo"></param>
        private void LoadAndParseCheckUpItems(String response)
        {
            response = response.Replace("[{\"total", "{\"total").Replace("]}]", "]}");
            List<CheckupInfo> list = new List<CheckupInfo>();
            CQCheckList cqInfo = JsonConvert.DeserializeObject<CQCheckList>(response);
            if (cqInfo.list != null && cqInfo.list.Length > 0)
            {
                for (int i = 0; i < cqInfo.list.Length; i++)
                {
                    CQCheckInfo item = cqInfo.list[i];
                    Utility.ClearNullValue<CQCheckInfo>(item);
                    CheckupInfo checkup = new CheckupInfo();
                    checkup.name = _enterpriseInfo.name;
                    checkup.reg_no = _enterpriseInfo.reg_no;
                    checkup.province = _enterpriseInfo.province;
                    checkup.department = item.insauth_cn;
                    checkup.type = item.instype;
                    checkup.date = string.IsNullOrEmpty(item.insdate) ? "" : item.insdate.Split(' ')[0];
                    checkup.result = item.insres_cn;
                    list.Add(checkup);
                }
            }
            _checkups = list;
        }
        #endregion

        #region 解析动产抵押详细信息
        private void LoadAndParseMortgageDetailInfo(List<ResponseInfo> responselist,MortgageInfo mortgageinfo)
        {
            foreach(ResponseInfo responseinfo in responselist)
            {
                if (responseinfo.Name == "mortperson")
                {
                    List<Mortgagee> mortgagees = new List<Mortgagee>();
                    var content = responseinfo.Data.Replace("[{\"total", "{\"total").Replace("]}]", "]}");
                    CQMortPersonList cqInfo = JsonConvert.DeserializeObject<CQMortPersonList>(content);
                    if (cqInfo.list != null && cqInfo.list.Length > 0)
                    {
                        for (int i = 0; i < cqInfo.list.Length; i++)
                        {
                            CQMortPerson item = cqInfo.list[i];
                            Utility.ClearNullValue<CQMortPerson>(item);
                            Mortgagee mortgagee = new Mortgagee();
                            mortgagee.seq_no = mortgagees.Count + 1;
                            mortgagee.name = item.more;
                            mortgagee.identify_type = item.blictype_cn;
                            mortgagee.identify_no = item.blicno;
                            mortgagees.Add(mortgagee);
                        }
                        mortgageinfo.mortgagees = mortgagees;
                    }
                }
                else if (responseinfo.Name == "mortguarantee")
                {
                    List<Guarantee> guarantees = new List<Guarantee>();// 抵押物概况
                    var content = responseinfo.Data.Replace("[{\"total", "{\"total").Replace("]}]", "]}");
                    CQMortGuaranteeList cqInfo = JsonConvert.DeserializeObject<CQMortGuaranteeList>(content);
                    if (cqInfo.list != null && cqInfo.list.Length > 0)
                    {
                        for (int j = 0; j < cqInfo.list.Length; j++)
                        {
                            CQMortGuarantee item = cqInfo.list[j];
                            Utility.ClearNullValue<CQMortGuarantee>(item);
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
                }
                else if (responseinfo.Name == "mortprincipalclaim")
                {
                    List<Guarantee> guarantees = new List<Guarantee>();// 抵押物概况
                    var content = responseinfo.Data.Replace("[{\"form", "{\"form").Replace("}}]", "}}");
                    CQMortPrincipalclaimList cqInfo = JsonConvert.DeserializeObject<CQMortPrincipalclaimList>(content);
                    if (cqInfo.form != null )
                    {
                        CQMortPrincipalclaim item = cqInfo.form;
                        Utility.ClearNullValue<CQMortPrincipalclaim>(item);
                        mortgageinfo.debit_type = item.priclaseckind_cn;
                        mortgageinfo.debit_amount = item.priclasecam == "" ? item.priclasecam : item.priclasecam;
                        mortgageinfo.debit_scope = item.warcov;
                        mortgageinfo.debit_period = string.Format("自 {0} 至{1}", item.pefperform, item.pefperto);
                        if (mortgageinfo.debit_period == "自  至") mortgageinfo.debit_period = "";
                        mortgageinfo.debit_remarks = item.remark;                        
                    }                   
                }
            }

        }
        #endregion

        #region 解析动产抵押登记信息
        //<summary>
         //解析动产抵押登记信息
         //</summary>
         //<param name="cqInfo"></param>
        private void LoadAndParseMortgageInfoItems(string response)
        {
            response = response.Replace("[{\"total", "{\"total").Replace("]}]", "]}");
            List<MortgageInfo> list = new List<MortgageInfo>();
            CQMotageList cqInfo = JsonConvert.DeserializeObject<CQMotageList>(response);
            if (cqInfo.list != null && cqInfo.list.Length > 0)
            {
                for (int i = 0; i < cqInfo.list.Length; i++)
                {
                    CQMotageListInfo item = cqInfo.list[i];
                    Utility.ClearNullValue<CQMotageListInfo>(item);
                    MortgageInfo mortgageinfo = new MortgageInfo();
                    mortgageinfo.seq_no = list.Count + 1;
                    mortgageinfo.number = item.morregcno;
                    mortgageinfo.date = item.regidate.Split(' ')[0];
                    mortgageinfo.amount = item.priclasecam == "" ? item.priclasecam : item.priclasecam + "万元";
                    mortgageinfo.status = item.type == "1" ? "有效" : "无效";
                    mortgageinfo.department =item.regorg_cn;
                    mortgageinfo.public_date = item.publicdate;
                    _request.AddOrUpdateRequestParameter("motage_id", item.morreg_id);
                    List<XElement> requestList = null;
                    List<ResponseInfo> responseList = null;
                    requestList = _requestXml.GetRequestListByGroup("motage").ToList();                   
                    responseList = GetResponseInfo(requestList);
                    LoadAndParseMortgageDetailInfo(responseList, mortgageinfo);                   
                    list.Add(mortgageinfo);
                }
            }
            _enterpriseInfo.mortgages = list;
        }
        #endregion

        #region 解析股权出质登记信息
        /// <summary>
        /// 解析股权出质登记信息
        /// </summary>
        /// <param name="cqInfo"></param>
        private void LoadAndParseEquityQualityItems(String  response)
        {
            response =  response.Replace("[{\"total", "{\"total").Replace("]}]", "]}");
            CQGqczList cqInfo =  JsonConvert.DeserializeObject<CQGqczList>(response); 
            List<EquityQuality> list = new List<EquityQuality>();
            if (cqInfo.list != null && cqInfo.list.Length > 0)
            {
                for (int i = 0; i < cqInfo.list.Length; i++)
                {
                     CQGqczInfo item = cqInfo.list[i];
                     Utility.ClearNullValue<CQGqczInfo>(item);
                    EquityQuality equityquality = new EquityQuality();
                    equityquality.seq_no = list.Count + 1;
                    equityquality.number = item.equityno;
                    equityquality.pledgor = item.pledgor;
                    equityquality.pledgor_identify_no = item.pledblicno;
                    equityquality.pledgor_amount = item.impam.ToString() + item.impam;
                    equityquality.pawnee = item.imporg;
                    equityquality.pawnee_identify_no = item.imporgblicno;
                    equityquality.date = string.IsNullOrEmpty(item.equpledate)?"":item.equpledate.Split(' ')[0];
                    equityquality.status = item.type_text;
                    equityquality.public_date = item.publicdate;
                    list.Add(equityquality);
                }
            }
            _enterpriseInfo.equity_qualities = list;
        }
        #endregion

        #region 解析行政许可信息
        /// <summary>
        /// 解析行政许可信息
        /// </summary>
        /// <param name="requestInfo"></param>
        private void LoadAndParseLicenseInfo(string response)
        {
            response = response.Replace("[{\"total", "{\"total").Replace("]}]", "]}");
            CQXzxkList xzxk = JsonConvert.DeserializeObject<CQXzxkList>(response);            
            List<LicenseInfo> list = new List<LicenseInfo>();
            if (xzxk.list != null && xzxk.list.Length > 0)
            {
                for (int i = 0; i < xzxk.list.Length; i++)
                {
                    CQXzxkInfo item = xzxk.list[i];
                    Utility.ClearNullValue<CQXzxkInfo>(item);
                    LicenseInfo licenseinfo = new LicenseInfo();
                    licenseinfo.seq_no = list.Count + 1;
                    licenseinfo.number = item.licno;
                    licenseinfo.name = item.licname_cn;
                    licenseinfo.start_date = item.valfrom.Split(' ')[0];
                    licenseinfo.end_date = item.valto;
                    licenseinfo.department = item.licanth;
                    licenseinfo.content = item.licitem;
                    //licenseinfo.status = item.isgs == "1" ? "有效" : "无效";
                    list.Add(licenseinfo);
                }
            }
            _enterpriseInfo.licenses = list;
        }
        #endregion

        #region 解析股东及出资信息
        /// <summary>
        /// 解析股东及出资信息
        /// </summary>
        /// <param name="requestInfo"></param>
        private void LoadAndParseFinancialContribution(string response)
        {
            //股东及出资信息
            response = response.Replace("[{\"total", "{\"total").Replace("]}]}]", "]}]}").Replace("list\":[]}]", "list\":[]}");

            CQFinancialList cqInfo = JsonConvert.DeserializeObject<CQFinancialList>(response);

            List<FinancialContribution> list = new List<FinancialContribution>();
            if (cqInfo.list != null && cqInfo.list.Length > 0)
            {
                for (int i = 0; i < cqInfo.list.Length; i++)
                {
                    CQFinancialInfo item = cqInfo.list[i];
                    Utility.ClearNullValue<CQFinancialInfo>(item);
                    FinancialContribution financialcontribution = new FinancialContribution();
                    financialcontribution.seq_no = list.Count + 1;
                    financialcontribution.investor_name = item.inv;
                    List<FinancialContribution.ShouldCapiItem> should_capi_items = new List<FinancialContribution.ShouldCapiItem>();
                    List<FinancialContribution.RealCapiItem> real_capi_items = new List<FinancialContribution.RealCapiItem>();
                    if (item.subList != null && item.subList.Length > 0)
                    {
                        decimal total_should_capi = 0;
                        decimal total_real_capi = 0;
                        foreach (var subItem in item.subList)
                        {
                            FinancialContribution.ShouldCapiItem CapiItem = new FinancialContribution.ShouldCapiItem();
                            CapiItem.should_invest_type = subItem.p_conform_cn;
                            CapiItem.should_capi = string.IsNullOrEmpty(subItem.p_subconam) ? "" : subItem.p_subconam ;
                            CapiItem.should_invest_date = string.IsNullOrEmpty(subItem.p_condate) ? "" : subItem.p_condate.Split(' ')[0];
                            CapiItem.public_date = string.IsNullOrEmpty(subItem.p_publicdate) ? "" : subItem.p_publicdate;
                            total_should_capi += string.IsNullOrEmpty(subItem.p_subconam) ? 0 : Convert.ToDecimal(subItem.p_subconam);
                            should_capi_items.Add(CapiItem);
                            FinancialContribution.RealCapiItem ReCapiItem = new FinancialContribution.RealCapiItem();
                            ReCapiItem.real_invest_type = string.IsNullOrEmpty(subItem.e_conform_cn) ? "" : subItem.e_conform_cn;
                            ReCapiItem.real_capi = string.IsNullOrEmpty(subItem.e_subconam) ? "" : subItem.e_subconam ;
                            ReCapiItem.real_invest_date = string.IsNullOrEmpty(subItem.e_condate) ? "" : subItem.e_condate.Split(' ')[0];
                            ReCapiItem.public_date = string.IsNullOrEmpty(subItem.e_publicdate) ? "" : subItem.e_publicdate;
                            total_real_capi += string.IsNullOrEmpty(subItem.e_subconam) ? 0 : Convert.ToDecimal(subItem.e_subconam);
                            real_capi_items.Add(ReCapiItem);
                        }
                        financialcontribution.total_should_capi = total_should_capi.ToString() ;
                        financialcontribution.should_capi_items = should_capi_items;
                        financialcontribution.total_real_capi = total_real_capi.ToString() ;
                        financialcontribution.real_capi_items = real_capi_items;
                    }

                    list.Add(financialcontribution);
                }
            }
        }
            
        #endregion

        #region 解析年报

        /// <summary>
        /// 解析年报
        /// </summary>

        /// <param name="cqReqort"></param>
        /// <param name="requestInfo"></param>
        private void LoadAndParseReports(string response)
        {
            response = response.Replace("[{\"total", "{\"total").Replace("]}]", "]}");
            CQReportList cqReqort = JsonConvert.DeserializeObject<CQReportList>(response);    
            List<Report> reportList = new List<Report>();
            if (cqReqort.list != null && cqReqort.list.Length > 0)
            {
                for (int i = 0; i < cqReqort.list.Length; i++)
                {
                    CQReportInfo item = cqReqort.list[i];
                    Report re = new Report();
                    re.report_year = item.ancheyear;
                    re.report_date = String.IsNullOrEmpty(item.anchedate) ? "" : item.anchedate.Split(' ')[0];
                    _request.AddOrUpdateRequestParameter("ancheid", item.ancheid);
                    List<XElement> requestList = null;
                    List<ResponseInfo> responseList = null;
                    if (item.anrpttype == "gtgshnb")
                    {
                        requestList = _requestXml.GetRequestListByGroup("gtreport").ToList();
                        responseList = GetResponseInfo(requestList);
                        LoadIndiviualReport(responseList, re);
                        reportList.Add(re);
                    }
                    else
                    {
                        requestList = _requestXml.GetRequestListByGroup("report").ToList();
                        responseList = GetResponseInfo(requestList);
                        LoadAndParseReportsDetail(responseList, re);
                        reportList.Add(re);
                    }

                    // this.LoadAndParseReports_Parallel(item, reportList));
                }
                //Parallel.ForEach(cqReqort.history, item => this.LoadAndParseReports_Parallel(item, requestInfo, reportList));
            }
            if (reportList.Any())
            {
                reportList.Sort(new ReportComparer());
                int i = 0;
                foreach (var report in reportList)
                {
                    i++;
                    report.ex_id = i.ToString();
                }
            }
            _enterpriseInfo.reports = reportList;
        }
        #endregion

        #region 解析年报基本信息--个体
        /// <summary>
        /// 解析年报详细页面
        /// </summary>
        /// <param name="responseList"></param>
        /// <param name="report"></param>
        private void LoadIndiviualReport(List<ResponseInfo> responseList, Report report)
        {
            foreach (ResponseInfo responseinfo in responseList)
            {
                if (responseinfo.Name == "basic")
                {
                    var content = responseinfo.Data.Replace("[{", "{").Replace("}]", "}");
                    CQGtReportBaseList cqre = JsonConvert.DeserializeObject<CQGtReportBaseList>(content);
                    if (cqre.form != null )
                    {
                        CQGtReportBaseInfo jsonreport = cqre.form;
                        Utility.ClearNullValue<CQGtReportBaseInfo>(jsonreport);
                        report.name = string.IsNullOrEmpty(jsonreport.traname) ? string.Empty : jsonreport.traname;
                        report.oper_name = string.IsNullOrEmpty(jsonreport.name) ? string.Empty : jsonreport.name;
                        report.reg_no = string.IsNullOrEmpty(jsonreport.regno) ? string.Empty : jsonreport.regno;
                        report.telephone = string.IsNullOrEmpty(jsonreport.tel) ? string.Empty : jsonreport.tel;
                        report.total_equity = string.IsNullOrEmpty(jsonreport.fundam) ? string.Empty : jsonreport.fundam;
                        report.collegues_num = string.IsNullOrEmpty(jsonreport.empnum) ? string.Empty : jsonreport.empnum;
                        report.sale_income = jsonreport.ratgrodis == "0" ? "个体工商户选择不公示" : jsonreport.ratgro;
                        report.tax_total = jsonreport.vendincdis == "0" ? "个体工商户选择不公示" : jsonreport.vendinc;

                    }
                }
            }
        }
        #endregion

        #region 解析年报详细页面
        /// <summary>
        /// 解析年报详细页面
        /// </summary>
        /// <param name="cqReqort"></param>
        private void LoadAndParseReportsDetail(List<ResponseInfo> responseList, Report report)
        {
            
            //基本信息
            foreach (ResponseInfo responseinfo in responseList)
            {
                if (responseinfo.Name == "basic")
                {
                    var content = responseinfo.Data.Replace("[{", "{").Replace("}]", "}");
                    CQReportBaseList cqre = JsonConvert.DeserializeObject<CQReportBaseList>(content);
                    if (cqre.form != null)
                    {
                        CQReportBaseInfo reportDetail = cqre.form;
                        Utility.ClearNullValue<CQReportBaseInfo>(reportDetail);
                        report.name =reportDetail.entname;
                        report.reg_no = reportDetail.regno;
                        report.telephone = reportDetail.tel;
                        report.address = reportDetail.addr;
                        report.zip_code = reportDetail.postalcode;
                        report.email = reportDetail.email;
                        report.if_invest = reportDetail.hasbrothers == "1" ? "是" : "否";
                        report.if_website = reportDetail.haswebsite == "1" ? "是" : "否";
                        report.status = reportDetail.busst_cn;
                        report.collegues_num = reportDetail.empnum;
                        report.if_external_guarantee = reportDetail.hasexternalsecurity == "1" ? "是" : "否";
                        report.if_invest = reportDetail.maibusincdis == "1" ? "是" : "否";
                        report.if_equity = reportDetail.istransfer == "1" ? "是" : "否";
                        report.total_equity = string.IsNullOrEmpty(reportDetail.assgro) ? "企业选择不公示" : reportDetail.assgro == "企业选择不公示" ? reportDetail.assgro : reportDetail.assgro + "万元人民币";
                        report.sale_income = string.IsNullOrEmpty(reportDetail.vendinc) ? "企业选择不公示" : reportDetail.vendinc == "企业选择不公示" ? reportDetail.vendinc : reportDetail.vendinc + "万元人民币";
                        report.serv_fare_income = string.IsNullOrEmpty(reportDetail.maibusinc) ? "企业选择不公示" : reportDetail.maibusinc == "企业选择不公示" ? reportDetail.maibusinc : reportDetail.maibusinc + "万元人民币";
                        report.tax_total = string.IsNullOrEmpty(reportDetail.ratgro) ? "企业选择不公示" : reportDetail.ratgro == "企业选择不公示" ? reportDetail.ratgro : reportDetail.ratgro + "万元人民币";
                        report.profit_reta = string.IsNullOrEmpty(reportDetail.totequ) ? "企业选择不公示" : reportDetail.totequ == "企业选择不公示" ? reportDetail.totequ : reportDetail.totequ + "万元人民币";
                        report.profit_total = string.IsNullOrEmpty(reportDetail.progro) ? "企业选择不公示" : reportDetail.progro == "企业选择不公示" ? reportDetail.progro : reportDetail.progro + "万元人民币";
                        report.net_amount = string.IsNullOrEmpty(reportDetail.netinc) ? "企业选择不公示" : reportDetail.netinc == "企业选择不公示" ? reportDetail.netinc : reportDetail.netinc + "万元人民币";
                        report.debit_amount = string.IsNullOrEmpty(reportDetail.liagro) ? "企业选择不公示" : reportDetail.liagro == "企业选择不公示" ? reportDetail.liagro : reportDetail.liagro + "万元人民币";

                    }
                }
                else if (responseinfo.Name == "website")
                {
                    var content = responseinfo.Data.Replace("[{\"list", "{\"list").Replace("}],", "},").Replace(",[{", ",{").Replace("[[", "[").Replace("]]", "]").Replace("]}]", "]}");
                    CQWebsiteList cqre = JsonConvert.DeserializeObject<CQWebsiteList>(content);
                    if (cqre.list != null && cqre.list.Length > 0)
                    {
                        List<WebsiteItem> websiteList = new List<WebsiteItem>();
                        for (int i = 0; i < cqre.list.Length; i++)
                        {
                            CQWebsiteInfo itemJson = cqre.list[i];
                            Utility.ClearNullValue<CQWebsiteInfo>(itemJson);
                            WebsiteItem item = new WebsiteItem();
                            item.seq_no = i + 1;
                            item.web_type = itemJson.webtype;
                            item.web_name = itemJson.websitname;
                            item.web_url = itemJson.website;
                            item.date = string.IsNullOrEmpty(itemJson.datainstime) ? "" : itemJson.datainstime.Split(' ')[0];
                            websiteList.Add(item);
                        }
                        report.websites = websiteList;
                    }
                }
                // 股东及出资
                else if (responseinfo.Name == "captial")
                {
                    var content = responseinfo.Data.Replace("[{\"total", "{\"total").Replace("]}]", "]}");
                    CQReportCapList cqre = JsonConvert.DeserializeObject<CQReportCapList>(content);
                    if (cqre.list != null && cqre.list.Length > 0)
                    {
                        List<Partner> partnerList = new List<Partner>();
                        for (int i = 0; i < cqre.list.Length; i++)
                        {
                            CQReportCapInfo itemJson = cqre.list[i];
                            Utility.ClearNullValue<CQReportCapInfo>(itemJson);
                            Partner item = new Partner();
                            item.seq_no = i + 1;
                            item.stock_name = itemJson.invname;
                            item.stock_type = itemJson.subconform_cn;
                            item.identify_no = "";
                            item.identify_type = "";
                            item.stock_percent = "";
                            item.ex_id = "";
                            item.should_capi_items = new List<ShouldCapiItem>();
                            item.real_capi_items = new List<RealCapiItem>();

                            ShouldCapiItem sItem = new ShouldCapiItem();
                            sItem.shoud_capi = string.IsNullOrEmpty(itemJson.lisubconam) ? "" : itemJson.lisubconam;
                            sItem.should_capi_date = string.IsNullOrEmpty(itemJson.subcondate) ? "" : itemJson.subcondate.Split(' ')[0];
                            sItem.invest_type = itemJson.subconform_cn;
                            item.should_capi_items.Add(sItem);
                            RealCapiItem rItem = new RealCapiItem();
                            rItem.real_capi = string.IsNullOrEmpty(itemJson.liacconam) ? "" : itemJson.liacconam;
                            rItem.real_capi_date = string.IsNullOrEmpty(itemJson.accondate) ? "" : itemJson.accondate.Split(' ')[0];
                            rItem.invest_type = itemJson.acconform_cn;
                            item.real_capi_items.Add(rItem);
                            partnerList.Add(item);
                        }
                        report.partners = partnerList;
                    }
                }
                //对外担保
                else if (responseinfo.Name == "externalguaran")
                {
                    var content = responseinfo.Data.Replace("[{\"total", "{\"total").Replace("]}]", "]}");
                    CQExternalGuaranList cqre = JsonConvert.DeserializeObject<CQExternalGuaranList>(content);
                    if (cqre.list != null && cqre.list.Length > 0)
                    {
                        List<ExternalGuarantee> guarantee_items = new List<ExternalGuarantee>();
                        for (int i = 0; i < cqre.list.Length; i++)
                        {
                            CQExternalGuaranInfo itemJson = cqre.list[i];
                            Utility.ClearNullValue<CQExternalGuaranInfo>(itemJson);
                            ExternalGuarantee item = new ExternalGuarantee();
                            item.creditor = itemJson.more;
                            item.debtor = itemJson.mortgagor;
                            item.type = itemJson.priclaseckind;
                            item.amount = itemJson.priclasecam;
                            item.period = itemJson.pefper;
                            item.guarantee_time = itemJson.guaranperiod;
                            item.guarantee_type = itemJson.gatype;
                            //item.guarant_scope = GetIsPublishValue(itemJson.ispublish, itemJson.rage);
                            guarantee_items.Add(item);
                        }
                        report.external_guarantees = guarantee_items;
                    }
                }
                //股权变更
                else if (responseinfo.Name == "stockchange")
                {
                    var content = responseinfo.Data.Replace("[{\"total", "{\"total").Replace("]}]", "]}");
                    CQReportStockChangeList cqre = JsonConvert.DeserializeObject<CQReportStockChangeList>(content);
                    if (cqre.list != null && cqre.list.Length > 0)
                    {
                        List<StockChangeItem> stockChanges = new List<StockChangeItem>();
                        for (int i = 0; i < cqre.list.Length; i++)
                        {
                            CQReportStockChangeInfo itemJson = cqre.list[i];
                            Utility.ClearNullValue<CQReportStockChangeInfo>(itemJson);
                            StockChangeItem item = new StockChangeItem();
                            item.seq_no = i + 1;
                            item.name = itemJson.inv;
                            item.before_percent = string.IsNullOrEmpty(itemJson.transampr) ? string.Empty : itemJson.transampr + "%";
                            item.after_percent = string.IsNullOrEmpty(itemJson.transamaft) ? string.Empty : itemJson.transamaft + "%";
                            item.change_date = itemJson.altdate;
                            item.public_date = string.Empty;
                            stockChanges.Add(item);
                        }
                        report.stock_changes = stockChanges;
                    }
                }
                //修改记录
                else if (responseinfo.Name == "alter")
                {
                    var content = responseinfo.Data.Replace("[{\"total", "{\"total").Replace("]}]", "]}");
                    CQReportsAlterList cqre = JsonConvert.DeserializeObject<CQReportsAlterList>(content);
                    if (cqre.list != null && cqre.list.Length > 0)
                    {
                        List<UpdateRecord> records = new List<UpdateRecord>();
                        for (int i = 0; i < cqre.list.Length; i++)
                        {
                            CQReportsAlterInfo itemJson = cqre.list[i];
                            Utility.ClearNullValue<CQReportsAlterInfo>(itemJson);
                            UpdateRecord item = new UpdateRecord();
                            item.seq_no = i + 1;
                            item.update_item = itemJson.alitem;
                            item.before_update = itemJson.altbe;
                            item.after_update = itemJson.altaf;
                            item.update_date = itemJson.altdate;
                            records.Add(item);
                        }
                        report.update_records = records;
                    }
                }
                // 对外投资
                else if (responseinfo.Name == "externalinvest")
                {
                    var content = responseinfo.Data.Replace("[{\"list", "{\"list").Replace("}],", "},").Replace(",[{", ",{").Replace("]]", "]").Replace("[[", "[").Replace("]}]", "]}");
                    CQExternalInvestList cqre = JsonConvert.DeserializeObject<CQExternalInvestList>(content);
                    if (cqre.list != null && cqre.list.Length > 0)
                    {
                        List<InvestItem> investList = new List<InvestItem>();
                        for (int i = 0; i < cqre.list.Length; i++)
                        {
                            CQExternalInvestInfo itemJson = cqre.list[i];
                            Utility.ClearNullValue<CQExternalInvestInfo>(itemJson);
                            InvestItem item = new InvestItem();
                            item.seq_no = i + 1;
                            item.invest_name = itemJson.entname;
                            item.invest_reg_no = itemJson.uniscid;
                            investList.Add(item);
                        }
                        report.invest_items = investList;
                    }
                }
                else if (responseinfo.Name == "xzck")
                {
                    var content = responseinfo.Data.Replace("[{\"total", "{\"list").Replace("}],", "},").Replace(",[{", ",{").Replace("]]", "]").Replace("[[", "[").Replace("]}]", "]}");
                    CQExternalInvestList cqre = JsonConvert.DeserializeObject<CQExternalInvestList>(content);
                    if (cqre.list != null && cqre.list.Length > 0)
                    {
                        List<InvestItem> investList = new List<InvestItem>();
                        for (int i = 0; i < cqre.list.Length; i++)
                        {
                            CQExternalInvestInfo itemJson = cqre.list[i];
                            Utility.ClearNullValue<CQExternalInvestInfo>(itemJson);
                            InvestItem item = new InvestItem();
                            item.seq_no = i + 1;
                            item.invest_name = itemJson.entname;
                            item.invest_reg_no = itemJson.uniscid;
                            investList.Add(item);
                        }
                        report.invest_items = investList;
                    }
                }
                else if (responseinfo.Name == "shebao")
                {
                    var content = responseinfo.Data.Replace("[{", "{").Replace("}]", "}");
                    CQReportSocialSecurityList cqre = JsonConvert.DeserializeObject<CQReportSocialSecurityList>(content);
                    if (cqre.form != null)
                    {
                        CQReportSocialSecurityInfo sbInfo = cqre.form;
                        Utility.ClearNullValue<CQReportSocialSecurityInfo>(sbInfo);
                        report.social_security.yanglaobx_num = string.IsNullOrWhiteSpace(sbInfo.so110) ? string.Empty : sbInfo.so110 + "人";
                        report.social_security.shiyebx_num = string.IsNullOrWhiteSpace(sbInfo.so210) ? string.Empty : sbInfo.so210 + "人";
                        report.social_security.yiliaobx_num = string.IsNullOrWhiteSpace(sbInfo.so310) ? string.Empty : sbInfo.so310 + "人";
                        report.social_security.gongshangbx_num = string.IsNullOrWhiteSpace(sbInfo.so410) ? string.Empty : sbInfo.so410 + "人";
                        report.social_security.shengyubx_num = string.IsNullOrWhiteSpace(sbInfo.so510) ? string.Empty : sbInfo.so510 + "人";
                        report.social_security.dw_yanglaobx_js = sbInfo.totalwages_so110;
                        report.social_security.dw_shiyebx_js = sbInfo.totalwages_so210;
                        report.social_security.dw_yiliaobx_js = sbInfo.totalwages_so310;
                        report.social_security.dw_shengyubx_js = sbInfo.totalwages_so510;
                        report.social_security.bq_yanglaobx_je = sbInfo.totalpayment_so110;
                        report.social_security.bq_shiyebx_je = sbInfo.totalpayment_so210;
                        report.social_security.bq_yiliaobx_je = sbInfo.totalpayment_so310;
                        report.social_security.bq_gongshangbx_je = sbInfo.totalpayment_so410;
                        report.social_security.bq_shengyubx_je = sbInfo.totalpayment_so510;
                        report.social_security.dw_yanglaobx_je = sbInfo.unpaidsocialins_so110;
                        report.social_security.dw_shiyebx_je = sbInfo.unpaidsocialins_so210;
                        report.social_security.dw_yiliaobx_je = sbInfo.unpaidsocialins_so310;
                        report.social_security.dw_gongshangbx_je = sbInfo.unpaidsocialins_so410;
                        report.social_security.dw_shengyubx_je = sbInfo.unpaidsocialins_so510;

                    }
                }

            }
        }
        #endregion
    }
}