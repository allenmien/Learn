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
using System.Configuration;
using MongoDB.Bson;
using System.Web;

namespace iOubo.iSpider.Infrastructure.Parser
{
    public class ConverterSD : IConverter
    {
        DataRequest _request;
        RequestInfo _requestInfo;
        RequestXml _requestXml;
        EnterpriseInfo _enterpriseInfo = new EnterpriseInfo();
        List<AbnormalInfo> _abnormals = new List<AbnormalInfo>();
        List<CheckupInfo> _checkups = new List<CheckupInfo>();
        List<string> reportsNeedToLoad = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings.Get("ReportsNeedToLoad"))
            ? new List<string>() : ConfigurationManager.AppSettings.Get("ReportsNeedToLoad").Split(',').ToList();
        Random _random = new Random();

        public SummaryEntity ProcessRequestAndParse(RequestInfo requestInfo)
        {
            if (!requestInfo.Parameters.ContainsKey("random"))
            {
                requestInfo.Parameters.Add("random", _random.NextDouble().ToString());
            }
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
            if (requestInfo.Parameters["type"] == "9999")
            {
                requestList = _requestXml.GetRequestListByGroup("gt").ToList();
            }
            else
            {
                requestList = _requestXml.GetRequestListByGroup("qy").ToList();
            }
            requestList.AddRange(_requestXml.GetRequestListByGroup("gov"));            
            List<ResponseInfo> responseList = GetResponseInfo(requestList);
            //foreach (ResponseInfo responseInfo in responseList)
            //{
            //    LoadData(responseInfo);
            //}
            Parallel.ForEach(responseList, responseInfo => LoadData(responseInfo));
            SummaryEntity summaryEntity = new SummaryEntity();
            summaryEntity.Enterprise = _enterpriseInfo;
            summaryEntity.Abnormals = _abnormals;
            summaryEntity.Checkups = _checkups;
            if (_enterpriseInfo.name == "海阳市文润果蔬种植专业合作社" || _enterpriseInfo.credit_no == "93370687MA3C1BELX8")
            {
                _enterpriseInfo.partners.Clear();
                _enterpriseInfo.employees.Clear();
            }
            return summaryEntity;
        }

        private void LoadData(ResponseInfo item)
        {
            switch (item.Name)
            {
                case "basic":
                    LoadAndParseBasic(item.Data, _enterpriseInfo);
                    break;
                case "gtbasic":
                    LoadAndParseGtBasic(item.Data, _enterpriseInfo);
                    break;
                case "jsxx":
                    LoadAndParsejsxx(item.Data, _enterpriseInfo);
                    break;
                case "sfxz":
                    LoadAndParseFreeze(item.Data, _enterpriseInfo);
                    break;
                case "report":
                    LoadAndParseReports(item.Data, _enterpriseInfo);
                    break;
            }
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

        #region 个体信息 
        private void LoadAndParseGtBasic(string response,EnterpriseInfo _enterpriseInfo)
        {         
            
            SDGtBaseList cqInfos = JsonConvert.DeserializeObject<SDGtBaseList>(response);
            SDGtBaseInfo cqInfo = cqInfos.jbxx;
            Utility.ClearNullValue<SDGtBaseInfo>(cqInfo);
             if (cqInfo.regno != null && cqInfo.regno.Length == 15)
                {
                    _enterpriseInfo.reg_no = cqInfo.regno;
                }
                else if (cqInfo.regno != null && cqInfo.regno.Length == 18)
                {
                    _enterpriseInfo.credit_no = cqInfo.regno;
                }
             var name = string.IsNullOrEmpty(cqInfo.traname) ? "" : cqInfo.traname;
             if (name == "null")
                 name = string.Empty;
            _enterpriseInfo.name = name;
            _enterpriseInfo.addresses.Add(new Address("注册地址", cqInfo.oploc, ""));
            _enterpriseInfo.belong_org = cqInfo.regorg;
            _enterpriseInfo.check_date = cqInfo.apprdate;
            _enterpriseInfo.econ_kind = "个体工商户";
            _enterpriseInfo.oper_name = string.IsNullOrEmpty(cqInfo.opername) ? "" : cqInfo.opername;            
            _enterpriseInfo.scope = string.IsNullOrEmpty(cqInfo.opscope) ? string.Empty : cqInfo.opscope;
            _enterpriseInfo.start_date = string.IsNullOrEmpty(cqInfo.estdate) ? string.Empty : cqInfo.estdate.Split(' ')[0];
            _enterpriseInfo.status = cqInfo.regstate == "1" ? "存续"
                                    : cqInfo.regstate == "2" ? "吊销"
                                            : cqInfo.regstate == "3" ? "注销"
                                                    : "迁出"; ;         
            _enterpriseInfo.type_desc = cqInfo.compform == "1" ? "个人经营" : "家庭经营";
            if (cqInfo.uniscid != null && Convert.ToString(cqInfo.uniscid).Length == 18)
            {
                _enterpriseInfo.credit_no = Convert.ToString(cqInfo.uniscid);
            }    
            if (cqInfos.bgsx != null && cqInfos.bgsx.Count() > 0)
            {
                List<ChangeRecord> changeRecordList = new List<ChangeRecord>();
                int count = 0;
                SDGtChangeInfo[] bgsx = cqInfos.bgsx;
                foreach (SDGtChangeInfo bginfo in bgsx)
                {
                    Utility.ClearNullValue<SDGtChangeInfo>(bginfo);
                    ChangeRecord changeRecord = new ChangeRecord();
                    changeRecord.change_item = bginfo.altitem;
                    changeRecord.before_content = bginfo.altbe;
                    changeRecord.after_content = bginfo.altaf;
                    changeRecord.change_date = string.IsNullOrEmpty(bginfo.altdate) ? string.Empty : bginfo.altdate;
                    changeRecord.seq_no = ++count;
                    changeRecordList.Add(changeRecord);
                }
                _enterpriseInfo.changerecords = changeRecordList;
            }
            if (cqInfos.xzcf != null && cqInfos.xzcf.Count() > 0)
            {
                this.LoadAndParseAdministrativePunishment(cqInfos.xzcf);
            }
        }
         #endregion

        #region
        string GetCfType(string cftype)
        {
            if (cftype == "01")
                return "警告";
            else if (cftype == "02")
                return "罚款";
            else if (cftype == "03")
                return "没收违法所得和非法财物";
            else if (cftype == "04")
                return "责令停产停业";
            else if (cftype == "05")
                return "暂扣许可证";
            else if (cftype == "06")
                return "暂扣执照(登记证)";
            else if (cftype == "07")
                return "吊销许可证";
            else if (cftype == "08")
                return "吊销执照(登记证)";
            else if (cftype == "09")
                return "法律、法规规定的其他行政处罚方式";
            else if (cftype == "1")
                return "警告";
            else if (cftype == "2")
                return "罚款";
            else if (cftype == "3")
                return "没收违法所得和非法财物";
            else if (cftype == "4")
                return "责令停产停业";
            else if (cftype == "5")
                return "暂扣许可证";
            else if (cftype == "6")
                return "暂扣执照(登记证)";
            else if (cftype == "7")
                return "吊销许可证";
            else if (cftype == "8")
                return "吊销执照(登记证)";
            else if (cftype == "9")
                return "法律、法规规定的其他行政处罚方式";
            return "罚款";
        }
         
        #endregion

        #region 工商登记信息
        private void LoadAndParseqybasic(Jbxx jbinfo, EnterpriseInfo _enterpriseInfo)
        {
            Utility.ClearNullValue<Jbxx>(jbinfo);
                if (jbinfo.regno != null && jbinfo.regno.Length == 15)
                {
                    _enterpriseInfo.reg_no = jbinfo.regno;
                }
                else if (jbinfo.regno != null && jbinfo.regno.Length == 18)
                {
                    _enterpriseInfo.credit_no = jbinfo.regno;
                }
               var name = string.IsNullOrEmpty(jbinfo.entname)?"":jbinfo.entname;
               if (name == "null")
                   name = string.Empty;
                _enterpriseInfo.name = name;
                _enterpriseInfo.econ_kind = jbinfo.enttype;
                _enterpriseInfo.oper_name = jbinfo.lerep;               
                Address address = new Address();
                address.name = "注册地址";
                address.address = jbinfo.dom;
                address.postcode = "";
                _enterpriseInfo.addresses.Add(address);    
               
                if (jbinfo.regcap != null &&jbinfo.regcap.Length > 0 )
                {
                    _enterpriseInfo.regist_capi = jbinfo.regcap + "万" + (jbinfo.regcapcur!=null?jbinfo.regcapcur:"元");
                }
                _enterpriseInfo.type_desc = string.IsNullOrEmpty(jbinfo.compform) ? string.Empty : jbinfo.compform== "1" ? "个人经营" : "家庭经营";
                _enterpriseInfo.start_date = string.IsNullOrEmpty(jbinfo.estdate) ? string.Empty : jbinfo.estdate;
                _enterpriseInfo.term_start = string.IsNullOrEmpty(jbinfo.opfrom) ? string.Empty : jbinfo.opfrom;
                _enterpriseInfo.term_end = string.IsNullOrEmpty(jbinfo.opto) ? string.Empty : jbinfo.opto; 
                _enterpriseInfo.scope = jbinfo.opscope;
                _enterpriseInfo.check_date = jbinfo.apprdate;
                _enterpriseInfo.status = jbinfo.regstate;
                _enterpriseInfo.belong_org = jbinfo.regorg;               
                if (jbinfo.uniscid != null && jbinfo.uniscid.Length == 18)
                {
                    _enterpriseInfo.credit_no = jbinfo.uniscid;
                }            
        }
         #endregion

        #region 工商变更信息
        private void LoadAndParseChangeRecord(Bgsx[] bgsx, EnterpriseInfo _enterpriseInfo)
        {            
            if (bgsx != null && bgsx.Count()>0)
            {
                List<ChangeRecord> changeRecordList = new List<ChangeRecord>();
                int count = 0;
                foreach (Bgsx bginfo in bgsx)
                {
                    Utility.ClearNullValue<Bgsx>(bginfo);
                    ChangeRecord changeRecord = new ChangeRecord();
                    changeRecord.change_item = bginfo.altitem;
                    changeRecord.before_content = bginfo.altbe;
                    changeRecord.after_content = bginfo.altaf;
                    changeRecord.change_date = string.IsNullOrEmpty(bginfo.altdate) ? string.Empty : bginfo.altdate;
                    changeRecord.seq_no = ++count;
                    changeRecordList.Add(changeRecord);
                }
                _enterpriseInfo.changerecords = changeRecordList;
            }
        }
        #endregion

        #region 工商股东信息
        private void LoadAndParseParter(CZxx[] info, EnterpriseInfo _enterpriseInfo)
        {
             if (info != null && info.Count()>0)
            {
                List<Partner> partners = new List<Partner>();
                foreach (CZxx item in info)
                {
                    Utility.ClearNullValue<CZxx>(item);
                    Partner partner = new Partner();
                    partner.identify_no = item.blicno==null?string.Empty:item.blicno;
                    partner.identify_type = item.blictype==null?string.Empty:item.blictype;
                    partner.stock_name = item.inv;
                    partner.stock_type = item.invtype;
                    partner.seq_no = _enterpriseInfo.partners.Count + partners.Count + 1;
                    partner.should_capi_items = new List<ShouldCapiItem>();
                    partner.real_capi_items = new List<RealCapiItem>();
                    // 加载股东出资详细信息                         
                    _request.AddOrUpdateRequestParameter("recid", item.recid);
                    List<ResponseInfo> responseList = GetResponseInfo(_requestXml.GetRequestListByName("gscz"));
                    if (responseList != null && responseList.Count > 0 && !string.IsNullOrWhiteSpace(responseList[0].Data))
                    {
                        SDCzInfo czinfo = JsonConvert.DeserializeObject<SDCzInfo>(responseList[0].Data);
                        foreach (gsRJXX temp in czinfo.rjs)
                        {
                            ShouldCapiItem rjx = new ShouldCapiItem();
                            if (temp.subconam != null && temp.subconam.Length > 0)
                            {
                                rjx.shoud_capi = string.IsNullOrEmpty(temp.subconam) ? string.Empty : temp.subconam + "万元";
                            }
                            rjx.should_capi_date = temp.condate;
                            rjx.invest_type = GetInvestType(temp.conform);
                            partner.should_capi_items.Add(rjx);
                        }
                        partner.total_should_capi = string.IsNullOrEmpty(item.lisubconam) ? string.Empty : item.lisubconam + "万元";
                        foreach (gsSJXX temp in czinfo.sjs)
                        {
                            RealCapiItem sjx = new RealCapiItem();
                            if (temp.acconam != null && temp.acconam.Length > 0)
                            {
                                sjx.real_capi = string.IsNullOrEmpty(temp.acconam) ? string.Empty : temp.acconam + "万元";
                            }
                            sjx.real_capi_date = temp.condate;
                            sjx.invest_type = GetInvestType(temp.conform);
                            partner.real_capi_items.Add(sjx);
                        }
                        partner.total_real_capi = string.IsNullOrEmpty(item.liacconam) ? string.Empty : item.liacconam + "万元";
                    }
                    partners.Add(partner);

                }
                _enterpriseInfo.partners = _enterpriseInfo.name == "中国建设银行股份有限公司青岛辽阳东路支行" ? new List<Partner>() : partners;
            }
        }
        #endregion

        #region 工商人员信息
        private void LoadAndParseEmployee(Ryxx[] info, EnterpriseInfo _enterpriseInfo)
        {
            if (info != null && info.Count() > 0)
            {
                List<Employee> employeeList = new List<Employee>();
                int count = 0;
                foreach (Ryxx item in info)
                {
                    Utility.ClearNullValue<Ryxx>(item);
                    Employee employee1 = new Employee();
                    employee1.job_title = item.position;
                    employee1.name = item.name;
                    employee1.seq_no = ++count;
                    employee1.sex = "";
                    employee1.cer_no = "";

                    employeeList.Add(employee1);
                }
                _enterpriseInfo.employees = employeeList;
            }
        }
        #endregion

        #region 工商分支机构
        private void LoadAndParseBranch(Fzjg[] info, EnterpriseInfo _enterpriseInfo)
        {
            if (info != null && info.Count() > 0)
            {
                List<Branch> branchList = new List<Branch>();
                int count = 0;
                foreach (Fzjg item in info)
                {
                    Utility.ClearNullValue<Fzjg>(item);
                    Branch branch = new Branch();
                    branch.belong_org = item.regorg;
                    branch.name = item.brname;
                    branch.seq_no = ++count;
                    branch.oper_name = "";
                    branch.reg_no = item.regno;
                    branchList.Add(branch);
                }
                _enterpriseInfo.branches = branchList;
            }
        }
        #endregion

        #region 动产抵押登记
        private void LoadAndParseMortgageInfoItems(DcdyDjxx[] jsonList, EnterpriseInfo _enterpriseInfo)
        {

            for (int i = 0; i < jsonList.Count(); i++)
            {
                DcdyDjxx item = jsonList[i];
                Utility.ClearNullValue<DcdyDjxx>(item);
                MortgageInfo mortgageinfo = new MortgageInfo();
                mortgageinfo.seq_no = _enterpriseInfo.mortgages.Count + 1;
                mortgageinfo.number = item.morregcno;
                mortgageinfo.date = item.regidate;
                mortgageinfo.amount = item.priclasecam == "" ? item.priclasecam : item.priclasecam + "万元";
                mortgageinfo.status = item.type == "1" ? "有效" : "无效";
                mortgageinfo.department = item.regorg;
                mortgageinfo.remarks = item.remark;
                mortgageinfo.scope = item.warcov;
                mortgageinfo.period = "自" + item.pefperform + " " + "至" + item.pefperto;
                mortgageinfo.public_date = item.pefperform;
                var request = this.CreateRequest();
                request.AddOrUpdateRequestParameter("mortgage_detail_id", HttpUtility.UrlEncode(item.morregcno).ToUpper());
                try
                {
                    var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("mortgage_detail"));
                    if (responseList != null && responseList.Any())
                    {

                        this.LoadAndParseMortgageDetail(responseList.First().Data, mortgageinfo);
                    }
                }
                catch { }
                _enterpriseInfo.mortgages.Add(mortgageinfo);
            }
        }
        #endregion

        #region 解析动产抵押详情信息
        void LoadAndParseMortgageDetail(string responseData, MortgageInfo mortgage)
        {
            if (!string.IsNullOrWhiteSpace(responseData))
            {
                BsonDocument document = BsonDocument.Parse(responseData);
                if (document != null)
                {
                    //抵押权人信息
                    if (document.Contains("dyqrxxs") && !document["dyqrxxs"].IsBsonNull)
                    {
                        BsonArray arr = document["dyqrxxs"].AsBsonArray;
                        if (arr != null && arr.Any())
                        {
                            foreach (BsonDocument item in arr)
                            {
                                Mortgagee mortgagee = new Mortgagee();
                                mortgagee.seq_no = mortgage.mortgagees.Count + 1;
                                mortgagee.name = item.Contains("more") ? (item["more"].IsBsonNull ? string.Empty : item["more"].AsString) : string.Empty;
                                mortgagee.identify_no = item.Contains("blicno") ? (item["blicno"].IsBsonNull ? string.Empty : item["blicno"].AsString) : string.Empty;
                                mortgagee.identify_type = item.Contains("blictype") ? (item["blictype"].IsBsonNull ? string.Empty : item["blictype"].AsString) : string.Empty;
                                mortgage.mortgagees.Add(mortgagee);
                            }
                        }
                    }
                    //被担保债权概况信息
                    if (document.Contains("zzqxx") && !document["zzqxx"].IsBsonNull)
                    {
                        BsonDocument item = document["zzqxx"].AsBsonDocument;
                        if (item != null)
                        {
                            mortgage.debit_type = item.Contains("priclaseckind") ? (item["priclaseckind"].IsBsonNull ? string.Empty : item["priclaseckind"].AsString) : string.Empty;
                            mortgage.debit_amount = this.ConvertBsonToStr("priclasecam", item);
                            mortgage.debit_scope = item.Contains("warcov") ? (item["warcov"].IsBsonNull ? string.Empty : item["warcov"].AsString) : string.Empty;
                            mortgage.debit_period = string.Format("{0}-{1}", item.Contains("pefperform") ? (item["pefperform"].IsBsonNull ? string.Empty : item["pefperform"].AsString) : string.Empty,
                                 item.Contains("pefperto") ? (item["pefperto"].IsBsonNull ? string.Empty : item["pefperto"].AsString) : string.Empty);
                            mortgage.debit_remarks = item.Contains("remark") ? (item["remark"].IsBsonNull ? string.Empty : item["remark"].AsString) : string.Empty;

                        }
                    }
                    //抵押物信息
                    if (document.Contains("dywxxs") && !document["dywxxs"].IsBsonNull)
                    {
                        BsonArray arr = document["dywxxs"].AsBsonArray;
                        if (arr != null && arr.Any())
                        {
                            foreach (BsonDocument item in arr)
                            {
                                Guarantee guarantee = new Guarantee();
                                guarantee.seq_no = mortgage.guarantees.Count + 1;
                                guarantee.name = item.Contains("guaname") ? (item["guaname"].IsBsonNull ? string.Empty : item["guaname"].AsString) : string.Empty;
                                guarantee.belong_to = item.Contains("own") ? (item["own"].IsBsonNull ? string.Empty : item["own"].AsString) : string.Empty;
                                guarantee.desc = item.Contains("guades") ? (item["guades"].IsBsonNull ? string.Empty : item["guades"].AsString) : string.Empty;
                                guarantee.remarks = item.Contains("remark") ? (item["remark"].IsBsonNull ? string.Empty : item["remark"].AsString) : string.Empty;
                                if (!string.IsNullOrWhiteSpace(guarantee.name))
                                {
                                    mortgage.guarantees.Add(guarantee);
                                }

                            }
                        }
                    }
                }

            }
        }
        #endregion

        #region convertamounttostr
        string ConvertBsonToStr(string key, BsonDocument document)
        {
            string result = string.Empty;
            if (document.Contains(key))
            {
                if (document[key].BsonType == BsonType.Int32)
                {
                    result = document[key].AsInt32.ToString();
                }
                else if (document[key].BsonType == BsonType.Int64)
                {
                    result = document[key].AsInt64.ToString();
                }
                else if (document[key].BsonType == BsonType.Double)
                {
                    result = document[key].AsDouble.ToString();
                }
            }

            return result;
        }
        #endregion

        #region 股权出质
        private void LoadAndParseEquityQuality(GqczDjxx[] jsonList, EnterpriseInfo _enterpriseInfo)
        {
            List<EquityQuality> list = new List<EquityQuality>();
            if (jsonList != null)
            {
                foreach (GqczDjxx item in jsonList)
                {
                    Utility.ClearNullValue<GqczDjxx>(item);
                    EquityQuality equityquality = new EquityQuality();
                    equityquality.seq_no = list.Count + 1;
                    equityquality.number = item.equityno;
                    equityquality.pledgor = item.pledgor;
                    equityquality.pledgor_identify_no = item.blicno;
                    equityquality.pledgor_amount = item.impam.ToString() + item.pledamunit;
                    equityquality.pawnee = item.imporg;
                    equityquality.pawnee_identify_no = item.impmorblicno;
                    equityquality.date = item.equpledate;
                    equityquality.status = item.type == "1" ? "有效" : "无效";
                    equityquality.public_date = item.equpledate;
                    list.Add(equityquality);
                }
            }
            _enterpriseInfo.equity_qualities = list;
        }
        #endregion

        #region 解析工商登记信息：基本信息、股东信息、变更信息、主要人员、分支机构
        private void LoadAndParseBasic(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            jbxxSD jsonList = JsonConvert.DeserializeObject<jbxxSD>(responseData);
            if (jsonList != null && jsonList.jbxx != null)
            {
                LoadAndParseqybasic(jsonList.jbxx, _enterpriseInfo);
            }
            if (jsonList != null && jsonList.czxx != null)
            {
                if (!("FGS" == jsonList.jbxx.entclass
                    || "HHFZ" == jsonList.jbxx.entclass
                    || "GRDZFZ" == jsonList.jbxx.entclass
                    || "WZFZ" == jsonList.jbxx.entclass
                    || "WZHHFZ" == jsonList.jbxx.entclass
                    || "WGJY" == jsonList.jbxx.entclass
                    || "HZSFZ" == jsonList.jbxx.entclass))
                {
                    LoadAndParseParter(jsonList.czxx, _enterpriseInfo);
                }
            }
            if (jsonList != null && jsonList.bgsx != null)
            {
                LoadAndParseChangeRecord(jsonList.bgsx, _enterpriseInfo);
            }
            if (jsonList != null && jsonList.ryxx != null)
            {
                LoadAndParseEmployee(jsonList.ryxx, _enterpriseInfo);
            }
            if (jsonList != null && jsonList.fzjg != null)
            {
                LoadAndParseBranch(jsonList.fzjg, _enterpriseInfo);
            }
            if (jsonList != null && jsonList.dcdyDjxx != null)
            {
                LoadAndParseMortgageInfoItems(jsonList.dcdyDjxx, _enterpriseInfo);
            }
            if (jsonList != null && jsonList.gqczDjxx != null)
            {
                LoadAndParseEquityQuality(jsonList.gqczDjxx, _enterpriseInfo);
            }
            if (jsonList != null && jsonList.jyyc != null)
            {
                LoadAndParseJingyin(jsonList.jyyc, _enterpriseInfo);
            }
            if (jsonList != null && jsonList.ccjcxx != null)
            {
                LoadAndParseChoucha(jsonList.ccjcxx, _enterpriseInfo);
            }
            if (jsonList != null && jsonList.xzcf != null)
            {
                LoadAndParseAdministrativePunishment(jsonList.xzcf);
            }
        }
        #endregion

        /// 解析企业自行提供的股权变更、行政许可、股东出资、行政处罚、知识产权登记
        private void LoadAndParsejsxx(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            jsxxSD jsonList = JsonConvert.DeserializeObject<jsxxSD>(responseData);
            if (jsonList != null && jsonList.xzxks != null)
            {
                LoadAndParseLicenseInfo(jsonList.xzxks, _enterpriseInfo);
            }
            if (jsonList != null && jsonList.czxxs != null)
            {
                LoadAndParseFinancialContribution(jsonList.czxxs, _enterpriseInfo);
            }
            if (jsonList != null && jsonList.gqbgs != null)
            {
                LoadAndParseStockChanges(jsonList.gqbgs, _enterpriseInfo);
            }

        }

        /// <summary>
        /// 解析行政许可信息
        /// </summary>
        /// <param name="requestInfo"></param>
        private void LoadAndParseLicenseInfo(XzxkSD[] json, EnterpriseInfo _enterpriseInf)
        {
            //行政许可信息
            List<LicenseInfo> list = new List<LicenseInfo>();
            if (json != null && json.Count() > 0)
            {
                for (int i = 0; i < json.Count(); i++)
                {
                    Xkxx item = json[i].xkxx;
                    Utility.ClearNullValue<Xkxx>(item);
                    LicenseInfo licenseinfo = new LicenseInfo();
                    licenseinfo.seq_no = list.Count + 1;
                    licenseinfo.number = item.licno;
                    licenseinfo.name = item.licname;
                    if (item.valfrom == null)
                    {
                        licenseinfo.start_date = string.Empty;
                    }
                    else
                    {
                        licenseinfo.start_date = item.valfrom;
                    }
                    if (item.valto == null)
                    {
                        licenseinfo.end_date = string.Empty;
                    }
                    else
                    {
                        licenseinfo.end_date = item.valto;
                    }
                    licenseinfo.department = item.licanth;
                    licenseinfo.content = item.licitem;
                    licenseinfo.status = item.type == "1" ? "有效" : "无效";

                    list.Add(licenseinfo);
                }
            }

            _enterpriseInfo.licenses = list;
        }

        #region 解析行政处罚信息
        /// <summary>
        /// 解析行政处罚信息
        /// </summary>
        /// <param name="xzcf"></param>
        void LoadAndParseAdministrativePunishment(SDGtAdministrativePunishment[] xzcf)
        {
            foreach (SDGtAdministrativePunishment item in xzcf)
            {
                Utility.ClearNullValue<SDGtAdministrativePunishment>(item);
                AdministrativePunishment ap = new AdministrativePunishment();
                ap.seq_no = _enterpriseInfo.administrative_punishments.Count + 1;
                ap.number = item.pendecno;
                ap.illegal_type = item.illegacttype;
                var cfstr = this.GetCfType(item.pentype);
                if (!string.IsNullOrWhiteSpace(item.penam))
                {
                    cfstr += "; 罚款金额:" + item.penam + "万元 ";
                }
                if (!string.IsNullOrWhiteSpace(item.forfam))
                {
                    cfstr += "; 没收金额:" + item.forfam + "万元";
                }
                ap.content = cfstr;
                ap.department = item.penauth;
                ap.date = item.pendecissdate;
                ap.public_date = item.pubdate;
                ap.description = item.remark;
                ap.name = item.uname;
                ap.oper_name = item.lerep;
                ap.reg_no = item.regno;
                _enterpriseInfo.administrative_punishments.Add(ap);
            }
        }
        #endregion

        #region 股权冻结
        private void LoadAndParseFreeze(string responseData, EnterpriseInfo _enterpriseInfo)
        {

            SfxzSD list = JsonConvert.DeserializeObject<SfxzSD>(responseData);
            List<JudicialFreeze> freezes = new List<JudicialFreeze>();

            if (list != null && list.sfxzs != null)
            {
                foreach (Sfxz item in list.sfxzs)
                {
                    Gqdj gqdj = item.gqdj;
                    Gqjd gqjd = item.gqjd;

                    JudicialFreeze freeze = new JudicialFreeze();
                    freeze.seq_no = freezes.Count + 1;
                    freeze.be_executed_person = gqdj.inv;
                    freeze.amount = gqdj.froam;
                    freeze.executive_court = gqdj.froauth;
                    freeze.number = string.IsNullOrEmpty(gqdj.executeno) ? string.Empty : gqdj.executeno;
                    freeze.status = GetStatus(gqdj.frozstate);
                    freeze.type = "股权冻结";
                    JudicialFreezeDetail detail = new JudicialFreezeDetail();
                    if (gqdj != null)
                    {
                        Utility.ClearNullValue<Gqdj>(gqdj);
                        detail.adjudicate_no = gqdj.frodocno;
                        detail.execute_court = gqdj.froauth;
                        detail.assist_name = gqdj.inv;
                        detail.assist_item = "公示冻结股权、其他投资权益";
                        detail.assist_ident_type = string.IsNullOrEmpty(gqdj.certype) ? string.Empty : gqdj.certype;
                        detail.assist_ident_no = string.IsNullOrEmpty(gqdj.cerno) ? string.Empty : gqdj.cerno;
                        detail.freeze_start_date = gqdj.frofrom;
                        detail.freeze_end_date = gqdj.froto;
                        detail.freeze_year_month = gqdj.frozdeadline;
                        detail.freeze_amount = gqdj.froam;
                        detail.notice_no = string.IsNullOrEmpty(gqdj.executeno) ? string.Empty : gqdj.executeno;
                        detail.public_date = gqdj.publicdate;
                        freeze.detail = detail;
                    }
                    if (gqjd != null)
                    {
                        Utility.ClearNullValue<Gqjd>(gqjd);
                        JudicialUnFreezeDetail un_freeze_detail = new JudicialUnFreezeDetail();
                        //Utility.ClearNullValue<JudicialUnFreezeDetail>(un_freeze_detail);
                        un_freeze_detail.adjudicate_no = gqjd.frodocno;
                        un_freeze_detail.execute_court = gqjd.froauth;
                        un_freeze_detail.assist_name = gqjd.inv;
                        un_freeze_detail.assist_item = "解除冻结股权、其他投资权益";
                        un_freeze_detail.assist_ident_type = string.IsNullOrEmpty(gqjd.certype) ? string.Empty : gqjd.certype;
                        un_freeze_detail.assist_ident_no = string.IsNullOrEmpty(gqjd.cerno) ? string.Empty : gqjd.cerno;
                        un_freeze_detail.unfreeze_date = gqjd.thawdate;
                        un_freeze_detail.freeze_amount = gqjd.froam;
                        un_freeze_detail.notice_no = string.IsNullOrEmpty(gqdj.executeno) ? string.Empty : gqdj.executeno;
                        un_freeze_detail.public_date = gqjd.publicdate;
                        freeze.un_freeze_detail = un_freeze_detail;
                        freeze.un_freeze_details.Add(un_freeze_detail);
                    }
                    if (item.gqsx != null)
                    {
                        Utility.ClearNullValue<Gqsx>(item.gqsx);
                        freeze.lose_efficacy.date = item.gqsx.loseeffdate;
                        freeze.lose_efficacy.reason = item.gqsx.loseeffres;
                    }
                    freezes.Add(freeze);
                }
            }
            if (list != null && list.sfgds != null)
            {

                foreach (var item in list.sfgds)
                {
                    JudicialFreeze freeze = new JudicialFreeze();
                    Utility.ClearNullValue<Sfgd>(item);
                    freeze.seq_no = freezes.Count + 1;
                    freeze.be_executed_person = item.inv;
                    freeze.amount = item.froam;
                    freeze.executive_court = item.froauth;
                    freeze.number = string.IsNullOrEmpty(item.executeno) ? string.Empty : item.executeno;
                    freeze.status = "股东变更";
                    freeze.type = "股权变更";

                    freeze.pc_freeze_detail.execute_court = item.froauth;
                    freeze.pc_freeze_detail.assist_item = "强制转让被执行人股权，办理有限责任公司股东变更登记";
                    freeze.pc_freeze_detail.adjudicate_no = item.frodocno;
                    freeze.pc_freeze_detail.notice_no = item.executeno;
                    freeze.pc_freeze_detail.assist_name = item.inv;
                    freeze.pc_freeze_detail.freeze_amount = item.froam;
                    freeze.pc_freeze_detail.assist_ident_type = item.blictype;
                    freeze.pc_freeze_detail.assist_ident_no = item.blicno;
                    freeze.pc_freeze_detail.assignee = item.salien;
                    freeze.pc_freeze_detail.xz_execute_date = item.executedate;
                    freeze.pc_freeze_detail.assignee_ident_type = item.sblictype;
                    freeze.pc_freeze_detail.assignee_ident_no = item.sblicno;
                    freezes.Add(freeze);
                }
            }
            _enterpriseInfo.judicial_freezes = freezes;
        }
        #endregion

        /// <summary>
        /// 解析企业股东及出资信息
        /// </summary>
        /// <param name="requestInfo"></param>
        private void LoadAndParseFinancialContribution(GuDongChuZiSD[] gdcz, EnterpriseInfo _enterpriseInfo)
        {
            //股东及出资信息          
            List<FinancialContribution> list = new List<FinancialContribution>();
            // GuDongChuZiSD[] gdcz = JsonConvert.DeserializeObject<GuDongChuZiSD[]>(responseData);
            if (gdcz != null)
            {
                for (int i = 0; i < gdcz.Count(); i++)
                {
                    GuDongChuZiSD item = gdcz[i];
                    if (item.rjxxs.Count == 0 && item.sjxxs.Count == 0)
                    {
                        continue;
                    }
                    Utility.ClearNullValue<GuDongChuZiSD>(item);
                    FinancialContribution financialcontribution = new FinancialContribution();
                    financialcontribution.seq_no = list.Count + 1;
                    financialcontribution.investor_name = item.czxx.inv;
                    financialcontribution.total_real_capi = item.czxx.liacconam;
                    financialcontribution.total_should_capi = item.czxx.lisubconam;
                    List<FinancialContribution.ShouldCapiItem> should_capi_items = new List<FinancialContribution.ShouldCapiItem>();
                    List<FinancialContribution.RealCapiItem> real_capi_items = new List<FinancialContribution.RealCapiItem>();

                    if (item.rjxxs != null && item.rjxxs.Count > 0)
                    {
                        foreach (RJXX subItem in item.rjxxs)
                        {
                            FinancialContribution.ShouldCapiItem CapiItem = new FinancialContribution.ShouldCapiItem();
                            CapiItem.should_invest_type = GetInvestType(subItem.conform);
                            CapiItem.should_capi = subItem.subconam;
                            CapiItem.should_invest_date = subItem.condate;
                            CapiItem.public_date = subItem.firstpubtime;
                            should_capi_items.Add(CapiItem);
                        }
                        financialcontribution.should_capi_items = should_capi_items;
                    }
                    if (item.sjxxs != null && item.sjxxs.Count > 0)
                    {
                        foreach (var acItem in item.sjxxs)
                        {
                            Utility.ClearNullValue<SJXX>(acItem);
                            FinancialContribution.RealCapiItem ReCapiItem = new FinancialContribution.RealCapiItem();
                            ReCapiItem.real_invest_type = GetInvestType(acItem.conform);
                            ReCapiItem.real_capi = acItem.acconam;
                            ReCapiItem.real_invest_date = acItem.condate;
                            ReCapiItem.public_date = acItem.firstpubtime;
                            real_capi_items.Add(ReCapiItem);
                        }
                        financialcontribution.real_capi_items = real_capi_items;
                    }
                    if (financialcontribution.should_capi_items != null && financialcontribution.should_capi_items.Any())
                    {
                        decimal should_total = 0;
                        foreach (var should_item in financialcontribution.should_capi_items)
                        {

                            decimal should_capi;
                            if (decimal.TryParse(should_item.should_capi, out should_capi))
                            {
                                should_total += should_capi;
                            }
                        }
                        financialcontribution.total_should_capi = should_total.ToString();
                    }
                    if (financialcontribution.real_capi_items != null && financialcontribution.real_capi_items.Any())
                    {
                        decimal real_total = 0;
                        foreach (var real_item in financialcontribution.real_capi_items)
                        {

                            decimal real_capi;
                            if (decimal.TryParse(real_item.real_capi, out real_capi))
                            {
                                real_total += real_capi;
                            }
                        }
                        financialcontribution.total_real_capi = real_total.ToString();
                    }
                    list.Add(financialcontribution);
                }
            }

            _enterpriseInfo.financial_contributions = list;
        }




        #region 解析股权变更信息
        private void LoadAndParseStockChanges(StockChangesSD[] jsonList, EnterpriseInfo _enterpriseInfo)
        {
            List<StockChangeItem> list = new List<StockChangeItem>();
            //StockChangesSD[] jsonList = JsonConvert.DeserializeObject<StockChangesSD[]>(responseData);
            for (int index = 0; index < jsonList.Count(); index++)
            {
                StockChangesSD item = jsonList[index];
                Utility.ClearNullValue<StockChangesSD>(item);
                StockChangeItem change = new StockChangeItem();
                change.seq_no = index + 1;
                change.name = item.inv;
                change.before_percent = string.IsNullOrEmpty(item.transamprpre) ? string.Empty : item.transamprpre + "%";
                change.after_percent = string.IsNullOrEmpty(item.transampraft) ? string.Empty : item.transampraft + "%";
                change.change_date = item.altdate;
                change.public_date = item.firstpubtime;
                list.Add(change);

            }
            _enterpriseInfo.stock_changes = list;
        }
        #endregion



        private string GetStatus(string code)
        {
            switch (code)
            {
                case "1":
                    return "冻结";
                case "2":
                    return "解除冻结";
                case "3":
                    return "失效";
            }
            return string.Empty;
        }





        #region 经营异常
        private void LoadAndParseJingyin(Jyyc[] jingYinList, EnterpriseInfo _enterpriseInfo)
        {
            List<AbnormalInfo> list = new List<AbnormalInfo>();
            if (jingYinList != null && jingYinList.Length > 0)
            {
                for (int i = 0; i < jingYinList.Length; i++)
                {
                    Jyyc item = jingYinList[i];
                    Utility.ClearNullValue<Jyyc>(item);
                    AbnormalInfo dItem = new AbnormalInfo();
                    dItem.name = _enterpriseInfo.name;
                    dItem.reg_no = _enterpriseInfo.reg_no;
                    dItem.province = _enterpriseInfo.province;
                    dItem.in_reason = item.specause == null ? "" : item.specause;
                    dItem.in_date = item.abntime == null ? "" : item.abntime;
                    dItem.out_reason = item.remexcpres;
                    dItem.out_date = item.remdate == null ? "" : item.remdate;
                    dItem.department = item.decorg;
                    list.Add(dItem);
                }
            }
            _abnormals = list;
        }
        #endregion



        /// <summary>
        /// 抽查检查
        /// </summary>
        /// <param name="cqInfo"></param>
        private void LoadAndParseChoucha(Ccjcxx[] chouChaList, EnterpriseInfo _enterpriseInfo)
        {
            List<CheckupInfo> list = new List<CheckupInfo>();
            if (chouChaList != null && chouChaList.Length > 0)
            {
                for (int i = 0; i < chouChaList.Length; i++)
                {
                    Ccjcxx item = chouChaList[i];
                    Utility.ClearNullValue<Ccjcxx>(item);
                    CheckupInfo checkup = new CheckupInfo();
                    checkup.name = item.entname;
                    checkup.reg_no = item.regno;
                    checkup.province = _enterpriseInfo.province;
                    checkup.department = item.insauthname;
                    checkup.type = item.instype == "1" ? "抽查" : "检查";
                    checkup.date = item.insdate;
                    checkup.result = item.insres;

                    list.Add(checkup);
                }
            }
            _checkups = list;
        }

        // <summary>
        /// 解析动产抵押登记信息
        /// </summary>
        /// <param name="cqInfo"></param>


        /// <summary>
        /// 解析动产抵押登记详情
        /// </summary>
        /// <param name="mortgageinfo"></param>
        /// <param name="response"></param>
        //private void LoadAndParseMortgageDetail(MortgageInfo mortgageinfo, string response)//当前山东未发现在详情页有数据
        //{
        //var matches = Regex.Matches(response, @"\[(.*?)\]", RegexOptions.Singleline | RegexOptions.Multiline);
        //if (matches.Count != 2) return;

        //MortgagerLN[] mortgagers = JsonConvert.DeserializeObject<MortgagerLN[]>(matches[0].Value);
        //List<Mortgagee> mortgagees = new List<Mortgagee>();// 抵押权人概况
        //if (mortgagers != null && mortgagers.Count() > 0)
        //{
        //    for (int j = 0; j < mortgagers.Count(); j++)
        //    {
        //        MortgagerLN item = mortgagers[j];
        //        Mortgagee mortgagee = new Mortgagee();
        //        mortgagee.seq_no = mortgagees.Count + 1;
        //        mortgagee.name = item.more;
        //        mortgagee.identify_type = item.certypeName;
        //        mortgagee.identify_no = item.cerno;
        //        mortgagees.Add(mortgagee);
        //    }
        //}
        //mortgageinfo.mortgagees = mortgagees;

        //PawnLN[] pawns = JsonConvert.DeserializeObject<PawnLN[]>(matches[1].Value);

        //List<Guarantee> guarantees = new List<Guarantee>();// 抵押物概况
        //if (pawns != null && pawns.Count() > 0)
        //{
        //    for (int j = 0; j < pawns.Count(); j++)
        //    {
        //        PawnLN item = pawns[j];
        //        Guarantee guarantee = new Guarantee();
        //        guarantee.seq_no = guarantees.Count + 1;
        //        guarantee.name = item.guaname;
        //        guarantee.belong_to = item.own;
        //        guarantee.desc = item.guades;
        //        guarantee.remarks = item.remark;
        //        guarantees.Add(guarantee);
        //    }
        //}
        //mortgageinfo.guarantees = guarantees;


        //HtmlDocument document = new HtmlDocument();
        //document.LoadHtml(response);
        //HtmlNode rootNode = document.DocumentNode;

        //List<MortgageInfo> MortgageList = new List<MortgageInfo>();

        //HtmlNodeCollection tables = rootNode.SelectNodes("//table[@class='detailsList']");
        //if (tables != null)
        //{
        //    foreach (HtmlNode table in tables)
        //    {
        //        var nodes = table.SelectNodes("./tr/th");
        //        if (nodes == null || nodes.Count == 0) continue;
        //        string header = nodes[0].InnerText.Trim();
        //        if (header.StartsWith("被担保债权概况"))
        //        {
        //            mortgageinfo.debit_type = table.SelectNodes("./tr/td")[0].InnerText;
        //            mortgageinfo.debit_amount = table.SelectNodes("./tr/td")[1].InnerText.Replace("\r\n", "").Replace("\n", "").Replace("\t", "");
        //            mortgageinfo.debit_scope = table.SelectNodes("./tr/td")[2].InnerText;
        //            mortgageinfo.debit_period = table.SelectNodes("./tr/td")[3].InnerText.Replace("\r\n", "").Replace("\n", "").Replace("\t", "");
        //            mortgageinfo.debit_remarks = table.SelectNodes("./tr/td")[4].InnerText.Replace("\r\n", "").Replace("\n", "").Replace("\t", "");
        //        }
        //    }
        //}
        //}









        private string Search_string(string s, string s1, string s2)
        {
            int n1, n2;
            n1 = s.IndexOf(s1, 0) + s1.Length;   //开始位置
            n2 = s.IndexOf(s2, n1);               //结束位置
            return s.Substring(n1, n2 - n1);   //取搜索的条数，用结束的位置-开始的位置,并返回
        }

        private string dateTimeTransfer(long time)
        {
            DateTime dt1970 = new DateTime(1970, 1, 1);
            return dt1970.AddMilliseconds(time).ToLocalTime().ToString("yyyy年MM月dd日");
        }

        private void LoadAndParseReports(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            try
            {

                List<Report> reportList = new List<Report>();
                responseData = "{" + "\"data\"" + ":" + responseData + "}";
                responseData = responseData.Replace("\\", "");

                SDReportInfo reports = JsonConvert.DeserializeObject<SDReportInfo>(responseData);
                Parallel.ForEach(reports.data, report => this.LoadAndParseReportDetail_Parallel(report));
                _enterpriseInfo.reports.Sort(new ReportComparer());


            }
            catch (Exception ex)
            {
                _enterpriseInfo.reports.Clear();
                Console.WriteLine("Exception when LoadAndParseReport.." + ex.ToString());
                LogHelper.Error("Exception when LoadAndParseReport.." + ex.ToString());
            }

        }
        private void LoadAndParseReportDetail_Parallel(SDreportjbxx report)
        {
            int flag = 0;
            Utility.ClearNullValue<SDreportjbxx>(report);
            Report re = new Report();
            re.report_year = report.ancheyear;
            re.report_date = report.firstpubtime;
            // 加载年报详细信息 
            if (report.ifpub != "5")
            {
                _request.AddOrUpdateRequestParameter("ancheid", report.ancheid);
                List<XElement> requestList = null;
                List<ResponseInfo> responseList = null;
                if (report.anchetype == "GT")
                {
                    requestList = _requestXml.GetRequestListByName("gtreport_detail").ToList();
                    responseList = GetResponseInfo(requestList);
                    flag = 1;
                }
                else
                {
                    requestList = _requestXml.GetRequestListByName("report_detail").ToList();
                    responseList = GetResponseInfo(requestList);
                    flag = 2;
                }
                if (responseList != null && responseList.Count > 0)
                {
                    LoadAndParseReportDetail(responseList[0].Data, re, flag);
                }
            }
            _enterpriseInfo.reports.Add(re);
        }
        private void LoadAndParseReportDetail(string responseData, Report report, int flag)
        {
            SDReportDetailInfo info = JsonConvert.DeserializeObject<SDReportDetailInfo>(responseData);
            /*
             * 基本信息
             */
            if (info != null && info.jbxx != null)
            {
                SDreportjbxx re = info.jbxx;
                Utility.ClearNullValue<SDreportjbxx>(re);
                if (re.uniscid != null && re.uniscid.Length == 18)
                {
                    report.credit_no = re.uniscid;
                }
                if (re.regno != null && re.regno.Length == 15)
                {
                    report.reg_no = re.regno;
                }
                var name = string.IsNullOrEmpty(re.entname) ? string.IsNullOrEmpty(re.traname) ? "" : re.traname : re.entname;
                report.name = name;

                report.telephone = re.tel;
                report.address = string.IsNullOrEmpty(re.addr) ? "" : re.addr;
                report.zip_code = string.IsNullOrEmpty(re.postalcode) ? string.Empty : re.postalcode;
                report.email = string.IsNullOrEmpty(re.email) ? string.Empty : re.email;
                report.status = string.IsNullOrEmpty(re.busst) ? "" : re.busst == "1" ? "开业" : re.busst == "4" ? "歇业" : "清算";
                report.if_website = string.IsNullOrEmpty(re.ifhasweb) ? "否" : re.ifhasweb == "1" ? "是" : "否";
                if (flag == 2)
                {
                    report.if_invest = string.IsNullOrEmpty(re.ifinvother) ? "否" : re.ifinvother == "1" ? "是" : "否";
                    report.if_external_guarantee = string.IsNullOrEmpty(info.dwdbs.ToString()) ? "否" : info.dwdbs.ToString().Length > 0 ? "是" : "否";
                    report.if_equity = string.IsNullOrEmpty(re.ifhasgqzr) ? "否" : re.ifhasgqzr == "1" ? "是" : "否";
                }
                else
                {
                    report.oper_name = re.name;
                    report.reg_capi = string.IsNullOrEmpty(re.fundam) ? string.Empty : re.fundam + "万";
                }
                report.collegues_num = string.IsNullOrEmpty(re.empnum) ? "企业选择不公示" : re.empnum;
            }
            /*
             * 股东出资信息
             */
            if (info != null && info.czxxs != null)
            {
                List<Partner> partnerList = new List<Partner>();
                int j = 1;
                foreach (SDczxx oneObj in info.czxxs)
                {
                    Utility.ClearNullValue<SDczxx>(oneObj);
                    Partner item = new Partner();
                    item.seq_no = j++;
                    item.stock_name = oneObj.inv;
                    item.stock_type = string.Empty;
                    item.should_capi_items = new List<ShouldCapiItem>();
                    item.real_capi_items = new List<RealCapiItem>();

                    ShouldCapiItem sItem = new ShouldCapiItem();
                    sItem.shoud_capi = string.IsNullOrEmpty(oneObj.lisubconam.ToString()) ? string.Empty : oneObj.lisubconam.ToString() + "万元";
                    sItem.should_capi_date = oneObj.subcondate;
                    sItem.invest_type = GetInvestType(oneObj.subconform);
                    item.should_capi_items.Add(sItem);

                    RealCapiItem rItem = new RealCapiItem();
                    rItem.real_capi = string.IsNullOrEmpty(oneObj.liacconam.ToString()) ? string.Empty : oneObj.liacconam.ToString() + "万元";
                    rItem.real_capi_date = oneObj.accondate;
                    rItem.invest_type = GetInvestType(oneObj.acconform);
                    item.real_capi_items.Add(rItem);

                    partnerList.Add(item);

                }
                report.partners = partnerList;
            }
            /*
             * 网店信息
             */
            if (info != null && info.wdxxs != null)
            {
                int j = 1;
                List<WebsiteItem> websiteList = new List<WebsiteItem>();
                foreach (SDwdxx oneObj in info.wdxxs)
                {
                    Utility.ClearNullValue<SDwdxx>(oneObj);
                    WebsiteItem item = new WebsiteItem();
                    item.seq_no = j++;
                    item.web_type = oneObj.webtype == "1" ? "网站" : "网店";
                    item.web_name = oneObj.websitname;
                    item.web_url = oneObj.domain;
                    websiteList.Add(item);
                }
                report.websites = websiteList;
            }
            /*
            * 对外投资信息
            */
            if (info != null && info.dwtzs != null)
            {
                List<InvestItem> investList = new List<InvestItem>();
                int j = 1;

                foreach (SDdwtz oneObj in info.dwtzs)
                {
                    Utility.ClearNullValue<SDdwtz>(oneObj);
                    InvestItem item = new InvestItem();
                    item.seq_no = j++;
                    item.invest_name = oneObj.entname;
                    item.invest_reg_no = oneObj.regno;
                    investList.Add(item);
                }
                report.invest_items = investList;
            }
            /*
            * 对外提供保证担保信息
            */
            if (info != null && info.dwdbs != null)
            {
                List<ExternalGuarantee> ext = new List<ExternalGuarantee>();
                int j = 1;

                foreach (SDdwdb oneObj in info.dwdbs)
                {
                    ExternalGuarantee item = new ExternalGuarantee();
                    Utility.ClearNullValue<SDdwdb>(oneObj);
                    item.seq_no = j++;
                    item.creditor = oneObj.more;
                    item.debtor = oneObj.mortgagor;
                    item.type = oneObj.priclaseckind == "1" ? "合同" : "其他";
                    item.amount = string.IsNullOrEmpty(oneObj.priclasecam) ? string.Empty : oneObj.priclasecam + "万元";
                    item.period = oneObj.pefperfrom + "-" + oneObj.pefperto;
                    item.guarantee_time = oneObj.guaranperiod == "1" ? "期限" : "未约定";
                    item.guarantee_type = oneObj.gatype == "1" ? "一般保证" : oneObj.gatype == "2" ? "连带保证" : "未约定";
                    item.guarantee_scope = GetRange(oneObj.gatype);
                    ext.Add(item);
                }
                report.external_guarantees = ext;
            }
            /*
           * 修改信息
           */
            if (info != null && info.alterHis != null)
            {
                List<UpdateRecord> records = new List<UpdateRecord>();
                int k = 1;
                foreach (SDalterHis oneObj in info.alterHis)
                {
                    Utility.ClearNullValue<SDalterHis>(oneObj);
                    UpdateRecord item = new UpdateRecord();
                    item.seq_no = k++;
                    item.update_item = oneObj.altfield;
                    item.before_update = oneObj.altbefore;
                    item.after_update = oneObj.altafter;
                    item.update_date = oneObj.altdate;
                    records.Add(item);
                }
                report.update_records = records;
            }
            /*
           * 股权变更信息
           */
            if (info != null && info.gqbgs != null)
            {
                List<StockChangeItem> ext = new List<StockChangeItem>();
                int j = 1;
                foreach (StockChangeSD oneObj in info.gqbgs)
                {
                    Utility.ClearNullValue<StockChangeSD>(oneObj);
                    StockChangeItem item = new StockChangeItem();
                    item.seq_no = j++;
                    item.name = oneObj.inv;
                    item.before_percent = string.IsNullOrEmpty(oneObj.transamprpre) ? string.Empty : oneObj.transamprpre + "%";
                    item.after_percent = string.IsNullOrEmpty(oneObj.transampraf) ? string.Empty : oneObj.transampraf + "%";
                    item.change_date = oneObj.altdate;
                    ext.Add(item);
                }
                report.stock_changes = ext;
            }
            /*
          * 资产状况信息
          */
            if (info != null && info.zczk != null)
            {
                if (flag == 2)
                {
                    SDzczk zczk = info.zczk;
                    Utility.ClearNullValue<SDzczk>(zczk);
                    report.total_equity = zczk.ifassgro == "1" ? zczk.assgro + "万元" : "企业选择不公示";
                    report.debit_amount = zczk.ifliagro == "1" ? zczk.liagro + "万元" : "企业选择不公示";
                    report.sale_income = zczk.ifvendinc == "1" ? zczk.vendinc + "万元" : "企业选择不公示";
                    report.serv_fare_income = zczk.ifmaibusinc == "1" ? zczk.maibusinc + "万元" : "企业选择不公示";
                    report.profit_total = zczk.ifprogro == "1" ? zczk.progro + "万元" : "企业选择不公示";
                    report.tax_total = zczk.ifratgro == "1" ? zczk.ratgro + "万元" : "企业选择不公示";
                    report.net_amount = zczk.ifnetinc == "1" ? zczk.netinc + "万元" : "企业选择不公示";
                    report.profit_reta = zczk.iftotequ == "1" ? zczk.totequ + "万元" : "企业选择不公示";
                }
                if (flag == 1)
                {
                    SDzczk zczk = info.zczk;
                    report.sale_income = zczk.ifvendinc == "1" ? zczk.vendinc + "万元" : "企业选择不公示";
                    report.tax_total = zczk.ifratgro == "1" ? zczk.ratgro + "万元" : "企业选择不公示";

                }

            }
            if (info != null && info.sbxx != null)
            {
                SDsbxx sbxx = info.sbxx;
                Utility.ClearNullValue<SDsbxx>(sbxx);
                var anchetype = sbxx.anchetype;
                report.social_security.yanglaobx_num = sbxx.so110;
                if (anchetype != "HZS")
                {
                    report.social_security.shiyebx_num = sbxx.so210;
                    report.social_security.gongshangbx_num = sbxx.so410;
                }
                report.social_security.yiliaobx_num = sbxx.so310;
                report.social_security.shengyubx_num = sbxx.so510;
                report.social_security.dw_yanglaobx_js = sbxx.totalwagesdis == "1" ? sbxx.totalwagesSo110 : "选择不公示";
                report.social_security.dw_shiyebx_js = sbxx.totalwagesdis == "1" ? sbxx.totalwagesSo210 : "选择不公示";
                report.social_security.dw_yiliaobx_js = sbxx.totalwagesdis == "1" ? sbxx.totalwagesSo310 : "选择不公示";
                report.social_security.dw_shengyubx_js = sbxx.totalwagesdis == "1" ? sbxx.totalwagesSo510 : "选择不公示";
                report.social_security.bq_yanglaobx_je = sbxx.totalpaymentdis == "1" ? sbxx.totalpaymentSo110 : "选择不公示";
                report.social_security.bq_shiyebx_je = sbxx.totalpaymentdis == "1" ? sbxx.totalpaymentSo210 : "选择不公示";
                report.social_security.bq_yiliaobx_je = sbxx.totalpaymentdis == "1" ? sbxx.totalpaymentSo310 : "选择不公示";
                report.social_security.bq_gongshangbx_je = sbxx.totalpaymentdis == "1" ? sbxx.totalpaymentSo410 : "选择不公示";
                report.social_security.bq_shengyubx_je = sbxx.totalpaymentdis == "1" ? sbxx.totalpaymentSo510 : "选择不公示";
                report.social_security.dw_yanglaobx_je = sbxx.unpaidsocialinsdis == "1" ? sbxx.unpaidsocialinsSo110 : "选择不公示";
                report.social_security.dw_shiyebx_je = sbxx.unpaidsocialinsdis == "1" ? sbxx.unpaidsocialinsSo210 : "选择不公示";
                report.social_security.dw_yiliaobx_je = sbxx.unpaidsocialinsdis == "1" ? sbxx.unpaidsocialinsSo310 : "选择不公示";
                report.social_security.dw_gongshangbx_je = sbxx.unpaidsocialinsdis == "1" ? sbxx.unpaidsocialinsSo410 : "选择不公示";
                report.social_security.dw_shengyubx_je = sbxx.unpaidsocialinsdis == "1" ? sbxx.unpaidsocialinsSo510 : "选择不公示";
            }
        }

   

        private string dateTimeTransfer(Updatetime date)
        {
            if (date == null) return "";
            DateTime dt1970 = new DateTime(1970, 1, 1);
            return dt1970.AddMilliseconds(date.time).ToLocalTime().ToString("yyyy年-MM月-dd日");
        }

        private string GetInvestType(string fs)
        {
            switch (fs)
            {
                case "1":
                    return "货币";
                case "2":
                    return "实物";
                case "3":
                    return "知识产权";
                case "4":
				    return "债权";			
			    case "6":
				    return "土地使用权";				
			    case "7":
				    return "股权,";			
			    case "9":
                    return "其他";                
            }
            return string.Empty;
        }

        private string GetRange(string range)
        {
            var rtn = "";
            var ranges = range.Split(',');
            for (var i = 0; i < ranges.Length; i++)
            {
                switch (ranges[i])
                {
                    case "1":
                        rtn += "主债权 ";
                        break;
                    case "2":
                        rtn += "利息 ";
                        break;
                    case "3":
                        rtn += "违约金 ";
                        break;
                    case "4":
                        rtn += "损害赔偿金 ";
                        break;
                    case "5":
                        rtn += "实现债权的费用 ";
                        break;
                    case "6":
                        rtn += "其他约定 ";
                        break;
                }
            }
            return rtn;

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
        //工商变更信息
       public class BGSX
       {
           public string openno { get; set; }
           public string altaf { get; set; }
           public string altbe { get; set; }
           public string altdate { get; set; }
           public string pripid { get; set; }
           public string altitem { get; set; }
       }
        //工商基本信息
       public class JBXX
       {
           public string pripid { get; set; }
           public string apprdate { get; set; }
           public string dom { get; set; }
           public string entname { get; set; }
           public string enttype { get; set; }
           public string estdate { get; set; }
           public string lerep { get; set; }
           public string opfrom { get; set; }
           public string opscope { get; set; }
           public string opto { get; set; }
           public string regcap { get; set; }
           public string regcapcur { get; set; }
           public string regno { get; set; }
           public string regorg { get; set; }
           public string regstate { get; set; }
           public string revdate { get; set; }
           public string uniscid { get; set; }
           public string xzqh { get; set; }
           public string entclass { get; set; }
           public string lereptype { get; set; }
           public string canreaname { get; set; }
           public string illegacttypename { get; set; }
       }
       //工商股东出资信息
       public class gsCZXX
       {
           public string blicno { get; set; }
           public string blictype { get; set; }
           public string inv { get; set; }
           public string invtype { get; set; }
           public string liacconam { get; set; }
           public string lisubconam { get; set; }  
           public string recid{get;set;}
          
       }
        //工商股东认缴信息
       public class gsRJXX
       {           
           public string inv { get; set; }
           public string conform { get; set; }
           public string condate { get; set; }                    
           public string subconam{get;set;}
       }
       //工商股东实缴信息
       public class gsSJXX
       {           
           public string inv { get; set; }
           public string conform { get; set; }
           public string condate { get; set; }                    
           public string subconam{get;set;}
           public string acconam { get; set; }
       }
       public class SDCzInfo
       {
           public string recid { get; set; }
           public gsCZXX czxx{ get; set; }
           public gsSJXX[] sjs { get; set; }
           public gsRJXX[] rjs { get; set; }


       }
        //分支机构
       public class FZJG
       {
           public string recid { get; set; }
           public string brname { get; set; }
           public string pripid { get; set; }
           public string regno { get; set; }
           public string regorg { get; set; }          

       }
       //人员信息
       public class RYXX
       {
           public string recid { get; set; }
           public string name { get; set; }
           public string position { get; set; }
           public string pripid { get; set; }
           public string blicno { get; set; }
           public string blictype { get; set; }

       }
        public class SDQyJbInfo
        {
            public JBXX jbxx { get; set; }
            public BGSX[] bgsx { get; set; }
            public gsCZXX[] czxx { get; set; }
            public FZJG[] fzjg { get; set; }
            public RYXX[] ryxx { get; set; } 
        }

    }
}
