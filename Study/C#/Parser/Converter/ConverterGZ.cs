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
    public class ConverterGZ : IConverter
    {
        DataRequest _request;
        RequestInfo _requestInfo;
        RequestXml _requestXml;
        EnterpriseInfo _enterpriseInfo = new EnterpriseInfo();
        List<AbnormalInfo> _abnormals = new List<AbnormalInfo>();
        List<CheckupInfo> _checkups = new List<CheckupInfo>();
        private bool isGt = false;
        List<string> reportsNeedToLoad = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings.Get("ReportsNeedToLoad"))
            ? new List<string>() : ConfigurationManager.AppSettings.Get("ReportsNeedToLoad").Split(',').ToList();
        int _parallelCount = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings.Get("ParallelCount")) ? 10 : int.Parse(ConfigurationManager.AppSettings.Get("ParallelCount"));
        public SummaryEntity ProcessRequestAndParse(RequestInfo requestInfo)
        {
            this._requestInfo = requestInfo;
            this._request = new DataRequest(requestInfo);
            if (requestInfo.Parameters["ztlx"] == "2")
            {
                isGt = true;
                if (requestInfo.Parameters.ContainsKey("platform") && "LIST_API" == requestInfo.Parameters["platform"])
                {
                    this._requestXml = new RequestXml(requestInfo.CurrentPath + "Gt_API", requestInfo.Province);
                }
                else
                {
                    this._requestXml = new RequestXml(requestInfo.CurrentPath, requestInfo.Province + "Gt");
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
                
            InitialEnterpriseInfo();

            //解析基本信息：基本信息、股东信息、变更信息、主要人员信息、分支机构信息、经营异常信息、抽查检查信息
            List<ResponseInfo> responseList = _request.GetResponseInfo(_requestXml.GetRequestListByGroup("basic"));
            Parallel.ForEach(responseList, new ParallelOptions() { MaxDegreeOfParallelism = _parallelCount }, responseInfo => ParseResponse(responseInfo));
            
            //ParseResponse(responseList);

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
                    case "basicInfo":
                        LoadAndParseBasicInfo(responseInfo.Data, _enterpriseInfo);
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
                    case "jingyin":
                        LoadAndParseAbnormal(responseInfo.Data, _enterpriseInfo);
                        break;
                    case "check":
                        LoadAndParseCheck(responseInfo.Data, _enterpriseInfo);
                        break;
                    case "report":
                        LoadAndParseReport(responseInfo.Data, _enterpriseInfo);
                        break;
                    case "dongchandiya":
                        LoadAndParseDongchandiya(responseInfo.Data, _enterpriseInfo);
                        break;
                    case "xingzhengxuke":
                        LoadAndParseXingZhengXuKe(responseInfo.Data, _enterpriseInfo);
                        break;
                    case "gudongjichuzi":
                        LoadAndParseGuDongJiChuZi(responseInfo.Data, _enterpriseInfo);
                        break;
                    case "guquandongjie":
                        LoadAndParseJudicialFreeze(responseInfo.Data, _enterpriseInfo);
                        break;
                    case "guquanbiangeng":
                        LoadAndParseGuQuanBianGeng(responseInfo.Data, _enterpriseInfo);
                        break;
                    default:
                        break;
                }
            }
        }

        #endregion

        #region 解析企业信息2
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
                case "jingyin":
                    LoadAndParseAbnormal(responseInfo.Data, _enterpriseInfo);
                    break;
                case "check":
                    LoadAndParseCheck(responseInfo.Data, _enterpriseInfo);
                    break;
                case "report":
                    LoadAndParseReport(responseInfo.Data, _enterpriseInfo);
                    break;
                case "dongchandiya":
                    LoadAndParseDongchandiya(responseInfo.Data, _enterpriseInfo);
                    break;
                case "xingzhengxuke":
                    LoadAndParseXingZhengXuKe(responseInfo.Data, _enterpriseInfo);
                    break;
                case "gudongjichuzi":
                    LoadAndParseGuDongJiChuZi(responseInfo.Data, _enterpriseInfo);
                    break;
                case "guquandongjie":
                    LoadAndParseJudicialFreeze(responseInfo.Data, _enterpriseInfo);
                    break;
                case "guquanbiangeng":
                    LoadAndParseGuQuanBianGeng(responseInfo.Data, _enterpriseInfo);
                    break;
                case "guquanchuzhi":
                    LoadAndParseGuQuanChuZi(responseInfo.Data, _enterpriseInfo);
                    break;
                case "other_xingzhengchufa":
                    LoadAndParseXZCF_Other(responseInfo.Data, _enterpriseInfo);
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
            if (!isGt)
            {
                BasicInfoGUIZHOU basicInfo = JsonConvert.DeserializeObject<BasicInfoGUIZHOU>(responseData);
                if (basicInfo.data != null && basicInfo.data.Length > 0)
                {
                    BasicInfoJsonGZ basicInfoGZ = basicInfo.data[0];
                    if (basicInfoGZ.zch.Contains("/"))
                    {
                        var numbers = basicInfoGZ.zch.Split('/');
                        foreach(var number in numbers)
                        {
                            if (number.Length == 18)
                            {
                                _enterpriseInfo.credit_no = number;
                            }
                            else
                            {
                                _enterpriseInfo.reg_no = number;
                            }
                        }
                    }
                    else
                    {
                        string tempZch = string.IsNullOrEmpty(basicInfoGZ.zch) ? "" : basicInfoGZ.zch;
                        if (tempZch.Length == 18)
                        {
                            _enterpriseInfo.credit_no = tempZch;
                        }
                        else
                        {
                            _enterpriseInfo.reg_no = tempZch;
                        }
                    }
//                    _enterpriseInfo.reg_no = string.IsNullOrEmpty(basicInfoGZ.zch) ? "" : basicInfoGZ.zch;
                    _enterpriseInfo.name = string.IsNullOrEmpty(basicInfoGZ.qymc) ? "" : basicInfoGZ.qymc.Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                    _enterpriseInfo.addresses.Add(new Address("注册地址", basicInfoGZ.zs, ""));
                    _enterpriseInfo.belong_org = string.IsNullOrEmpty(basicInfoGZ.djjgmc) ? "" : basicInfoGZ.djjgmc;
                    _enterpriseInfo.check_date = string.IsNullOrEmpty(basicInfoGZ.hzrq) ? "" : basicInfoGZ.hzrq;
                    _enterpriseInfo.econ_kind = string.IsNullOrEmpty(basicInfoGZ.qylxmc) ? "" : basicInfoGZ.qylxmc;
                    _enterpriseInfo.oper_name = string.IsNullOrEmpty(basicInfoGZ.fddbr) ? "" : basicInfoGZ.fddbr;
                    _enterpriseInfo.regist_capi = string.IsNullOrEmpty(basicInfoGZ.zczb) ? "" : basicInfoGZ.zczb;
                    _enterpriseInfo.scope = string.IsNullOrEmpty(basicInfoGZ.jyfw) ? "" : basicInfoGZ.jyfw;
                    _enterpriseInfo.start_date = string.IsNullOrEmpty(basicInfoGZ.clrq) ? "" : basicInfoGZ.clrq;
                    _enterpriseInfo.end_date = "";//basicInfoGZ.yyrq2;
                    _enterpriseInfo.status = string.IsNullOrEmpty(basicInfoGZ.mclxmc) ? "" : basicInfoGZ.mclxmc;
                    _enterpriseInfo.term_start = string.IsNullOrEmpty(basicInfoGZ.yyrq1) ? "" : basicInfoGZ.yyrq1;
                    _enterpriseInfo.term_end = string.IsNullOrEmpty(basicInfoGZ.yyrq2) ? "" : basicInfoGZ.yyrq2;
                }
            }
            else
            {
                var basicInfo = JsonConvert.DeserializeObject<BasicInfoGUIZHOUGt>(responseData);
                if (basicInfo.data != null && basicInfo.data.Length > 0)
                {
                    BasicInfoJsonGZGt basicInfoGZ = basicInfo.data[0];
                    string tempZch = string.IsNullOrEmpty(basicInfoGZ.zch) ? "" : basicInfoGZ.zch;
                    if (tempZch.Length == 18)
                    {
                        _enterpriseInfo.credit_no = tempZch;
                    }
                    else
                    {
                        _enterpriseInfo.reg_no = tempZch;
                    }
                    //_enterpriseInfo.reg_no = basicInfoGZ.zch;
                    _enterpriseInfo.name = string.IsNullOrWhiteSpace(basicInfoGZ.zhmc) ? string.Empty : basicInfoGZ.zhmc.Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                    _enterpriseInfo.addresses.Add(new Address("注册地址", basicInfoGZ.zs, ""));
                    _enterpriseInfo.belong_org = basicInfoGZ.djjgmc;
                    _enterpriseInfo.check_date = basicInfoGZ.hzrq;
                    _enterpriseInfo.econ_kind = "个体（内地）";
                    _enterpriseInfo.oper_name = basicInfoGZ.jyzm;
                    _enterpriseInfo.regist_capi = "";
                    _enterpriseInfo.scope = basicInfoGZ.jyfw;
                    _enterpriseInfo.start_date = basicInfoGZ.clrq;
                    _enterpriseInfo.end_date = "";//basicInfoGZ.yyrq2;
                    _enterpriseInfo.status = "";
                    _enterpriseInfo.term_start = string.IsNullOrWhiteSpace(basicInfoGZ.jyqsrq) ? "" : basicInfoGZ.jyqsrq;
                    _enterpriseInfo.term_end = string.IsNullOrWhiteSpace(basicInfoGZ.jyjzrq) ? "" : basicInfoGZ.jyjzrq;
                    _enterpriseInfo.type_desc = basicInfoGZ.zcxsmc;

                    var request = CreateRequest();
                    request.AddOrUpdateRequestParameter("qymc", _enterpriseInfo.name);
                    request.AddOrUpdateRequestParameter("zch", _enterpriseInfo.reg_no);
                    var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("basicInfostatus"));
                    if (responseList != null&&responseList.Any())
                    {
                        var basicGtInfo = JsonConvert.DeserializeObject<BasicInfoJsonGZGT>(responseList[0].Data);
                        if(basicGtInfo.data!=null && basicGtInfo.data.Count()>0)
                        {
                            _enterpriseInfo.status = basicGtInfo.data[0].mclxmc;
                        }
                    }
                }
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
            var request = CreateRequest();
            int n;
            List<Partner> partnerList = new List<Partner>();
            PartnerGUIZHOU partnerGZ = JsonConvert.DeserializeObject<PartnerGUIZHOU>(responseData);
            if (partnerGZ.data != null && partnerGZ.data.Length > 0)
            {
                for (int i = 0; i < partnerGZ.data.Length; i++)
                {
                    PartnerJsonGZ item = partnerGZ.data[i];
                    Partner partner = new Partner();
                    partner.identify_no = string.IsNullOrEmpty(item.zzbh) ? "" : item.zzbh;
                    partner.identify_type = string.IsNullOrEmpty(item.zzlxmc) ? "" : item.zzlxmc;
                    var name = string.IsNullOrEmpty(item.czmc) ? "" : item.czmc;

                    partner.stock_name = name;
                    partner.stock_type = string.IsNullOrEmpty(item.tzrlxmc) ? "" : item.tzrlxmc;
                    partner.seq_no = i + 1;
                    partner.stock_percent = "";
                    partner.should_capi_items = new List<ShouldCapiItem>();
                    partner.real_capi_items = new List<RealCapiItem>();

                    //股东详情
                    request.AddOrUpdateRequestParameter("czmc", item.czmc);
                    List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("investor"));
                    if (responseList != null && responseList.Count > 0)
                    {
                        PartnerDetailGUIZHOU partnerDetail = JsonConvert.DeserializeObject<PartnerDetailGUIZHOU>(responseList[0].Data);
                        if (partnerDetail.data != null && partnerDetail.data.Length > 0)
                        {
                            PartnerDetailJsonGZ partnerDetailJson = partnerDetail.data[0];
                            partner.total_should_capi = convertCash(string.IsNullOrEmpty(partnerDetailJson.rjcze) ? "" : partnerDetailJson.rjcze);
                            partner.total_real_capi = convertCash(string.IsNullOrEmpty(partnerDetailJson.sjcze) ? "" : partnerDetailJson.sjcze);
                            ShouldCapiItem shouldItem = new ShouldCapiItem();
                            RealCapiItem realItem = new RealCapiItem();
                            shouldItem.shoud_capi = convertCash(string.IsNullOrEmpty(partnerDetailJson.rjcze) ? "" : partnerDetailJson.rjcze);
                            shouldItem.should_capi_date = string.IsNullOrEmpty(partnerDetailJson.rjczrq) ? "" : partnerDetailJson.rjczrq;
                            shouldItem.invest_type = string.IsNullOrEmpty(partnerDetailJson.rjczfsmc) ? "" : partnerDetailJson.rjczfsmc;
                            realItem.real_capi = convertCash(string.IsNullOrEmpty(partnerDetailJson.sjcze) ? "" : partnerDetailJson.sjcze);
                            realItem.real_capi_date = string.IsNullOrEmpty(partnerDetailJson.sjczrq) ? "" : partnerDetailJson.sjczrq;
                            realItem.invest_type = string.IsNullOrEmpty(partnerDetailJson.sjczfsmc) ? "" : partnerDetailJson.sjczfsmc;
                            if (!string.IsNullOrWhiteSpace(shouldItem.shoud_capi) && !shouldItem.shoud_capi.StartsWith("万"))
                            {
                                partner.should_capi_items.Add(shouldItem);
                            }
                            if (!string.IsNullOrWhiteSpace(realItem.real_capi) && !realItem.real_capi.StartsWith("万"))
                            {
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

        #region 变更信息
        /// <summary>
        /// 变更信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseAlter(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            List<ChangeRecord> changeRecordList = new List<ChangeRecord>();
            AlterGUIZHOU alterGZ = JsonConvert.DeserializeObject<AlterGUIZHOU>(responseData);
            if (alterGZ.data != null && alterGZ.data.Length > 0)
            {
                for (int i = 0; i < alterGZ.data.Length; i++)
                {
                    AlterJsonGZ item = alterGZ.data[i];
                    ChangeRecord changeRecord = new ChangeRecord();
                    changeRecord.change_item = item.bcsxmc;
                    changeRecord.before_content = item.bcnr;
                    changeRecord.after_content = item.bghnr;
                    changeRecord.change_date = item.hzrq;
                    changeRecord.seq_no = i + 1;

                    changeRecordList.Add(changeRecord);
                }
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
            if (isGt)
            {
                EmployeeGUIZHOUGT employeeGZ = JsonConvert.DeserializeObject<EmployeeGUIZHOUGT>(responseData);
                if (employeeGZ.data != null && employeeGZ.data.Length > 0)
                {
                    for (int i = 0; i < employeeGZ.data.Length; i++)
                    {
                        EmployeeJsonGZGT item = employeeGZ.data[i];
                        Employee employee1 = new Employee();
                        employee1.job_title ="";
                        employee1.name = item.jyzm;
                        employee1.seq_no = item.rownum;
                        employee1.sex = "";
                        employee1.cer_no = "";

                        employeeList.Add(employee1);
                    }
                }
                _enterpriseInfo.employees = employeeList;
            }
            else
            {
                EmployeeGUIZHOU employeeGZ = JsonConvert.DeserializeObject<EmployeeGUIZHOU>(responseData);
                if (employeeGZ.data != null && employeeGZ.data.Length > 0)
                {
                    for (int i = 0; i < employeeGZ.data.Length; i++)
                    {
                        EmployeeJsonGZ item = employeeGZ.data[i];
                        Employee employee1 = new Employee();
                        employee1.job_title = item.zwmc;
                        employee1.name = item.xm;
                        employee1.seq_no = i + 1;
                        employee1.sex = "";
                        employee1.cer_no = "";

                        employeeList.Add(employee1);
                    }
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
            BranchGUIZHOU branchGZ = JsonConvert.DeserializeObject<BranchGUIZHOU>(responseData);
            if (branchGZ.data != null && branchGZ.data.Length > 0)
            {
                for (int i = 0; i < branchGZ.data.Length; i++)
                {
                    BranchJsonGZ item = branchGZ.data[i];
                    Branch branch = new Branch();
                    branch.belong_org = item.fgsdjjgmc;
                    branch.name = item.fgsmc;
                    branch.seq_no = i + 1;
                    branch.oper_name = "";
                    branch.reg_no = item.fgszch;

                    branchList.Add(branch);
                }
            }
            _enterpriseInfo.branches = branchList;
        }
        #endregion

        #region 解析动产抵押
        private void LoadAndParseDongchandiya(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            var request = CreateRequest();
            DongchandiyaJsonGUIZHOU json= JsonConvert.DeserializeObject<DongchandiyaJsonGUIZHOU>(responseData);
            if (json != null && json.successed && json.data.Any())
            {
                foreach (var item in json.data)
                {
                    MortgageInfo info = new MortgageInfo();
                    info.seq_no = item.rownum;
                    info.number = item.djbh;
                    info.date = item.djrq;
                    info.department = item.djjgmc;
                    info.amount = item.bdbse;
                    info.status = item.zt;
                    info.public_date = item.gsrq;
                    request.AddOrUpdateRequestParameter("dcnbxh", item.dcnbxh);
                    request.AddOrUpdateRequestParameter("djbh", item.djbh);
                    List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("dongchandiyaDetail"));
                    if (responseList != null && responseList.Any())
                    {
                        foreach (var response in responseList)
                        {
                            if (response.Name == "dongchandiyaDetail_diyaquanren")
                            {
                                DongchandiyaDetailRenJsonGUIZHOU renJson = JsonConvert.DeserializeObject<DongchandiyaDetailRenJsonGUIZHOU>(response.Data);
                                if(renJson!=null&&renJson.successed&&renJson.data.Any())
                                {
                                    foreach (var ren in renJson.data)
                                    {
                                        Mortgagee mortgagee = new Mortgagee()
                                        {
                                            seq_no = ren.rownum,
                                            name = ren.dyqrmc,
                                            identify_no = ren.zjhm,
                                            identify_type = ren.zjlx
                                        };
                                        info.mortgagees.Add(mortgagee);
                                    }   
                                }
                            }
                            else if (response.Name == "dongchandiyaDetail_beidanbaozhaiquan")
                            {
                                DongchandiyaDetaiZhaiQuanJsonGUIZHOU zqJson = JsonConvert.DeserializeObject<DongchandiyaDetaiZhaiQuanJsonGUIZHOU>(response.Data);
                                if (zqJson != null && zqJson.successed && zqJson.data.Any())
                                {
                                    var first = zqJson.data.First();
                                    info.debit_amount = first.bdbse;
                                    info.debit_scope = first.dbfw;
                                    info.debit_type = first.bdbzl;
                                    info.debit_period = first.qx;
                                    info.debit_remarks = first.bz == null ? "" : first.bz;
                                }
                            }
                            else if (response.Name == "dongchandiyaDetail_diyawu")
                            {

                                DongchandiyaDetailWuJsonGUIZHOU wuJson = JsonConvert.DeserializeObject<DongchandiyaDetailWuJsonGUIZHOU>(response.Data);
                                if (wuJson != null && wuJson.successed && wuJson.data.Any())
                                {
                                    foreach (var wu in wuJson.data)
                                    {
                                        Guarantee guarantee = new Guarantee()
                                         {

                                             seq_no = wu.rownum,
                                             belong_to = wu.syq,
                                             name = wu.mc,
                                             desc = wu.xq,
                                             remarks = string.IsNullOrWhiteSpace(wu.bz) ? "" : wu.bz
                                         };
                                        info.guarantees.Add(guarantee);
                                    }
                                }
                            }
                            else
                            {
                                DongchandiyaDetaiZhaiQuanJsonGUIZHOU zqJson = JsonConvert.DeserializeObject<DongchandiyaDetaiZhaiQuanJsonGUIZHOU>(response.Data);
                                if (zqJson != null && zqJson.successed && zqJson.data.Any())
                                {
                                    var first = zqJson.data.First();
                                    info.scope = first.dbfw;
                                    info.type = first.bdbzl;
                                    info.period = first.qx;
                                    info.remarks = first.bz == null ? "" : first.bz;
                                }
                            }
                        }
                        _enterpriseInfo.mortgages.Add(info);
                    }
                }
            }
        }
        #endregion

        #region 行政许可
        private void LoadAndParseXingZhengXuKe(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            XingzhengxukeJsonGUIZHOU xzxk = JsonConvert.DeserializeObject<XingzhengxukeJsonGUIZHOU>(responseData);
            if (xzxk != null && xzxk.successed && xzxk.data.Any())
            {
                foreach (var item in xzxk.data)
                {
                    LicenseInfo license = new LicenseInfo()
                    {
                        seq_no = item.rownum,
                        number = item.xkwjbh,
                        start_date = item.ksyxqx,
                        end_date = item.jsyxqx,
                        department = item.xkjg,
                        name = item.xkwjmc,
                        content = item.xknr,
                        status=item.zt
                    };
                    _enterpriseInfo.licenses.Add(license);
                }
            }
            
        }
        #endregion

        #region 股东及出资信息
        private void LoadAndParseGuDongJiChuZi(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            GudongjichuziJsonGUIZHOU gdjcz = JsonConvert.DeserializeObject<GudongjichuziJsonGUIZHOU>(responseData);
            if (gdjcz != null && gdjcz.successed && gdjcz.data.Any())
            {
                var c = 1;
                foreach (var item in gdjcz.data)
                {

                    FinancialContribution financialContribution = new FinancialContribution()
                    {
                        seq_no = c,
                        investor_name = item.tzrmc,
                        total_should_capi = item.ljrje,
                        total_real_capi = item.ljsje,
                        //should_capi_items = new List<ShouldCapiItem>(),
                        //real_capi_items= new List<RealCapiItem>()
                    };
                    FinancialContribution.ShouldCapiItem sci = new FinancialContribution.ShouldCapiItem();
                    sci.should_capi = item.rjcze;
                    sci.should_invest_date = item.rjczrq;
                    sci.should_invest_type = item.rjczfs;
                    sci.public_date = item.sjgsrq;
                    financialContribution.should_capi_items.Add(sci);

                    FinancialContribution.RealCapiItem rci = new FinancialContribution.RealCapiItem();
                    rci.real_capi = item.sjcze;
                    rci.real_invest_date = item.sjczrq;
                    rci.real_invest_type = item.sjczfs;
                    rci.public_date = item.sjgsrq;
                    financialContribution.real_capi_items.Add(rci);

                    _enterpriseInfo.financial_contributions.Add(financialContribution);
                    c++;
                }
            }

        } 
        #endregion
         
        #region 股权变更
        void LoadAndParseGuQuanBianGeng(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            GuquanbiangengJsonGUIZHOU gqbg = JsonConvert.DeserializeObject<GuquanbiangengJsonGUIZHOU>(responseData);
            if (gqbg != null && gqbg.successed && gqbg.data.Any())
            {
                foreach (var item in gqbg.data)
                {
                    StockChangeItem scItem = new StockChangeItem()
                    {
                        seq_no = item.rownum,
                        name = item.gd,
                        before_percent = item.bgqbl,
                        after_percent = item.bghbl,
                        change_date = item.bgrq,
                        public_date = item.gsrq
                    };

                    _enterpriseInfo.stock_changes.Add(scItem);
                }
            }
        }
        #endregion

        #region 股权冻结
        void LoadAndParseJudicialFreeze(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            var request = CreateRequest();
            JudicialFreezeJsonGUIZHOU judicialFreeze = JsonConvert.DeserializeObject<JudicialFreezeJsonGUIZHOU>(responseData);
            if (judicialFreeze != null && judicialFreeze.successed && judicialFreeze.data.Any())
            {
                foreach (var item in judicialFreeze.data)
                {
                    JudicialFreeze jf = new JudicialFreeze()
                    {
                        seq_no = item.rownum,
                        be_executed_person =item.bzxr,
                        amount = item.gqse,
                        executive_court = item.zxfy,
                        number = item.xzgstzswh,
                        status = item.ztmc,
                        detail = new JudicialFreezeDetail()
                        
                    };
                    request.AddOrUpdateRequestParameter("guquandongjieDetaillId", item.id);
                    List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("guquandongjieDetail"));
                    if (responseList != null && responseList.Any())
                    {
                        LoadAndParseJudicialFreezeDetailOne(responseList[0].Data,jf);
                        LoadAndParseJudicialFreezeDetailTwo(responseList[1].Data, jf);
                    }
                    _enterpriseInfo.judicial_freezes.Add(jf);
                }
            }
            
        }
        #endregion

        #region 股权冻结详情
        void LoadAndParseJudicialFreezeDetailOne(string responseData, JudicialFreeze jf)
        {
            if (!string.IsNullOrWhiteSpace(responseData))
            {
                JudicialFreezeDetailJsonGUIZHOU json = JsonConvert.DeserializeObject<JudicialFreezeDetailJsonGUIZHOU>(responseData);
                if (json != null && json.successed && json.data.Any())
                {
                    var first = json.data.First();
                    JudicialFreezeDetail detail = new JudicialFreezeDetail();
                    detail.execute_court = first.zxfy;//执行法院
                    detail.assist_item = first.zxsxmc;//执行事项
                    detail.adjudicate_no = first.zxcdwh;//执行裁定书文号
                    detail.notice_no = first.zxtzwh;//执行通知书文号
                    detail.assist_name = first.bzxr;//被执行人
                    detail.freeze_amount = first.gqse;//被执行人持有股权、其它投资权益的数额
                    detail.assist_ident_type = first.zjlxmc;//被执行人证件种类
                    detail.assist_ident_no = first.zjhm;//被执行人证件号码
                    detail.freeze_start_date = first.djksrq;//冻结期限自
                    detail.freeze_end_date = first.djjsrq;//冻结期限至
                    detail.freeze_year_month = first.djqx;//冻结期限
                    detail.public_date = first.gsrq;//公示日期
                    jf.detail = detail;
                }

            }
        }
        #endregion

        #region 股权冻结解冻详情
        void LoadAndParseJudicialFreezeDetailTwo(string responseData, JudicialFreeze jf)
        {
            if (!string.IsNullOrWhiteSpace(responseData))
            {
                JudicialFreezeDetailJsonTwoGUIZHOU json = JsonConvert.DeserializeObject<JudicialFreezeDetailJsonTwoGUIZHOU>(responseData);
                if (json != null && json.successed && json.data.Any())
                {
                    var first = json.data.First();
                    JudicialUnFreezeDetail detail = new JudicialUnFreezeDetail();
                    detail.execute_court = first.zxfy;//执行法院
                    detail.assist_item = string.Format("{0}（{1}）",first.zxsxmc,first.zxsxsm);//执行事项
                    detail.adjudicate_no = first.zxcdwh;//执行裁定书文号
                    detail.notice_no = first.zxtzwh;//执行通知书文号
                    detail.assist_name = first.bzxr;//被执行人
                    detail.freeze_amount = first.gqse;//被执行人持有股权、其它投资权益的数额
                    detail.assist_ident_type = first.zjlxmc;//被执行人证件种类
                    detail.assist_ident_no = first.zjhm;//被执行人证件号码
                    detail.unfreeze_date = first.jdrq;//解除冻结期限
                    detail.public_date = first.gsrq;//公示日期
                    jf.un_freeze_detail = detail;
                }

            }
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

            JingyinGUIZHOU jsonList = JsonConvert.DeserializeObject<JingyinGUIZHOU>(responseData);
            if (jsonList.data != null && jsonList.data.Length > 0)
            {
                for (int i = 0; i < jsonList.data.Length; i++)
                {
                    JingyinJsonGZ item = jsonList.data[i];
                    AbnormalInfo dItem = new AbnormalInfo();
                    dItem.name = _enterpriseInfo.name;
                    dItem.reg_no = _enterpriseInfo.reg_no;
                    dItem.province = _enterpriseInfo.province;
                    dItem.in_reason = item.lryy + "\n" + item.lrjdswh;
                    dItem.in_date = item.lrrq;
                    dItem.out_reason = item.ycyy;
                    dItem.out_date = item.ycrq;
                    dItem.department = item.zcjdjg;

                    abnList.Add(dItem);
                }
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
            CheckGUIZHOU check = JsonConvert.DeserializeObject<CheckGUIZHOU>(responseData);
            if (check.data != null && check.data.Length > 0)
            {
                for (int i = 0; i < check.data.Length; i++)
                {
                    CheckJsonGZ item = check.data[i];
                    CheckupInfo checkup = new CheckupInfo();
                    checkup.name = _enterpriseInfo.name;
                    checkup.reg_no = _enterpriseInfo.reg_no;
                    checkup.province = _enterpriseInfo.province;
                    checkup.department = item.ssjg;
                    checkup.type = item.cclx;
                    checkup.date = item.ccrq;
                    checkup.result = item.ccjg;

                    list.Add(checkup);
                }
            }
            _checkups = list;
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
                ReportGUIZHOU reportGZ = JsonConvert.DeserializeObject<ReportGUIZHOU>(responseData);

                Utility.ClearNullValue<ReportGUIZHOU>(reportGZ);
                if (reportGZ.data != null && reportGZ.data.Length > 0)
                {
                    Parallel.ForEach(reportGZ.data, new ParallelOptions { MaxDegreeOfParallelism = _parallelCount }, item => this.LoadAndParseReport_Parallel(item));
                    _enterpriseInfo.reports.Sort(new ReportComparer());
                    int i = 1;
                    foreach (var report in _enterpriseInfo.reports)
                    {
                        report.ex_id = i.ToString();
                        i++;
                    }
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

        #region 解析年报信息-并行
        void LoadAndParseReport_Parallel(ReportJsonGZ item)
        {
            var request = CreateRequest();
            Report report = new Report();
            report.ex_id = "";
            report.report_name = item.nd + "年度报告";
            report.report_year = item.nd;
            report.report_date = item.rq;
            if (!reportsNeedToLoad.Any() || reportsNeedToLoad.Contains(report.report_year))
            {
                // 详细年报 
                if (item.lsh != null)
                {
                    request.AddOrUpdateRequestParameter("lsh", item.lsh);
                    List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByGroup("report"));
                    ParseReport(responseList, report);
                }

                _enterpriseInfo.reports.Add(report);
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
                    case "reportUpdateRecord":
                        LoadAndParseReportUpdateRecord(responseInfo.Data, report);
                        break;
                    default:
                        break;
                }
            }
        }
        #endregion

        #region 加载解析年报基本信息
        /// <summary>
        /// 加载解析年报基本信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportBasic(string responseData, Report report)
        {
            if (isGt)
            {
                ReportBasicInfoGUIZHOUGT basicInfo = JsonConvert.DeserializeObject<ReportBasicInfoGUIZHOUGT>(responseData);
                ReportBasicInfoJsonGZGT reportDetail = basicInfo.data[0];
                Utility.ClearNullValue<ReportBasicInfoJsonGZGT>(reportDetail);

                report.name = reportDetail.sjmc.Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                report.reg_no = reportDetail.sjzch;
                report.reg_capi = reportDetail.zjse;
                report.oper_name = reportDetail.sjjyz;
                report.telephone = reportDetail.lxdh;
                report.collegues_num = reportDetail.cyrs;
               
            }
            else
            {
                ReportBasicInfoGUIZHOU basicInfo = JsonConvert.DeserializeObject<ReportBasicInfoGUIZHOU>(responseData);
                ReportBasicInfoJsonGZ reportDetail = basicInfo.data[0];
                Utility.ClearNullValue<ReportBasicInfoJsonGZ>(reportDetail);
                report.name = reportDetail.qymc.Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                report.reg_no = reportDetail.zch;
                report.telephone = reportDetail.lxdh;
                report.address = reportDetail.dz;
                report.zip_code = reportDetail.yzbm;
                report.email = reportDetail.dzyx;
                report.if_invest = reportDetail.sfdw;
                report.if_website = reportDetail.sfww;
                report.status = reportDetail.jyzt;
                report.collegues_num = reportDetail.cyrs;
                report.if_equity = reportDetail.sfzr;
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

            ReportWebsiteGUIZHOU reportDetail = JsonConvert.DeserializeObject<ReportWebsiteGUIZHOU>(responseData);

            if (reportDetail.data != null && reportDetail.data.Length > 0)
            {
                for (int i = 0; i < reportDetail.data.Length; i++)
                {
                    ReportWebsiteJsonGZ itemJson = reportDetail.data[i];
                    WebsiteItem item = new WebsiteItem();

                    item.seq_no = i + 1;
                    item.web_type = itemJson.lx;
                    item.web_name = itemJson.mc;
                    item.web_url = itemJson.wz;

                    websiteList.Add(item);
                }
            }

            report.websites = websiteList;
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
            List<Partner> partnerList = new List<Partner>();

            ReportPartnerGUIZHOU reportDetail = JsonConvert.DeserializeObject<ReportPartnerGUIZHOU>(responseData);
            if (reportDetail.data != null && reportDetail.data.Length > 0)
            {
                for (int i = 0; i < reportDetail.data.Length; i++)
                {
                    ReportPartnerJsonGZ itemJson = reportDetail.data[i];

                    Partner item = new Partner();
                    item.seq_no = i + 1;
                    item.stock_name = itemJson.tzr;
                    item.stock_type = "";
                    item.identify_no = "";
                    item.identify_type = "";
                    item.stock_percent = "";
                    item.ex_id = "";
                    item.should_capi_items = new List<ShouldCapiItem>();
                    item.real_capi_items = new List<RealCapiItem>();

                    ShouldCapiItem sItem = new ShouldCapiItem();
                    sItem.shoud_capi = itemJson.rjcze;
                    sItem.should_capi_date = itemJson.rjczrq;
                    sItem.invest_type = itemJson.rjczfs;
                    item.should_capi_items.Add(sItem);

                    RealCapiItem rItem = new RealCapiItem();
                    rItem.real_capi = itemJson.sjcze;
                    rItem.real_capi_date = itemJson.sjczrq;
                    rItem.invest_type = itemJson.sjczfs;
                    item.real_capi_items.Add(rItem);

                    partnerList.Add(item);
                }
            }

            report.partners = partnerList;
        }
        #endregion

        #region LoadAndParseReportInvest
        private void LoadAndParseReportInvest(string responseData, Report report)
        {
            List<InvestItem> investList = new List<InvestItem>();
            ReportInvestGUIZHOU reportDetail = JsonConvert.DeserializeObject<ReportInvestGUIZHOU>(responseData);
            if (reportDetail.data != null && reportDetail.data.Length > 0)
            {
                for (int i = 0; i < reportDetail.data.Length; i++)
                {
                    ReportInvestJsonGZ itemJson = reportDetail.data[i];
                    InvestItem item = new InvestItem();

                    item.seq_no = i + 1;
                    item.invest_name = itemJson.mc;
                    item.invest_reg_no = itemJson.zch;

                    investList.Add(item);
                }
            }

            report.invest_items = investList;
        }
        #endregion

        #region LoadAndParsereportZichan
        private void LoadAndParsereportZichan(string responseData, Report report)
        {
            if (isGt)
            {
                ReportZichanGUIZHOUGT reportGZ = JsonConvert.DeserializeObject<ReportZichanGUIZHOUGT>(responseData);

                if (reportGZ.data != null && reportGZ.data.Length > 0)
                {
                    ReportZichanJsonGZGT reportDetail = reportGZ.data[0];
                    // "营业总收入":
                    report.sale_income = string.IsNullOrEmpty(reportDetail.xse) ? "" : reportDetail.xse.Replace(" ", "");
                    // "纳税总额":
                    report.tax_total = string.IsNullOrEmpty(reportDetail.nsze) ? "" : reportDetail.nsze.Replace(" ", "");
                }
            }
            else
            {
                ReportZichanGUIZHOU reportGZ = JsonConvert.DeserializeObject<ReportZichanGUIZHOU>(responseData);

                if (reportGZ.data != null && reportGZ.data.Length > 0)
                {
                    ReportZichanJsonGZ reportDetail = reportGZ.data[0];

                    // "资产总额":
                    report.total_equity = string.IsNullOrEmpty(reportDetail.zcze) ? "" : reportDetail.zcze.Replace(" ", "");
                    // "负债总额":
                    report.debit_amount = string.IsNullOrEmpty(reportDetail.fzze) ? "" : reportDetail.fzze.Replace(" ", "");
                    // "营业总收入":
                    report.sale_income = string.IsNullOrEmpty(reportDetail.xsze) ? "" : reportDetail.xsze.Replace(" ", "");
                    // "营业总收入中主营业务收入":
                    report.serv_fare_income = string.IsNullOrEmpty(reportDetail.zysr) ? "" : reportDetail.zysr.Replace(" ", "");
                    // "利润总额":
                    report.profit_total = string.IsNullOrEmpty(reportDetail.lrze) ? "" : reportDetail.lrze.Replace(" ", "");
                    // "净利润":
                    report.net_amount = string.IsNullOrEmpty(reportDetail.jlr) ? "" : reportDetail.jlr.Replace(" ", "");
                    // "纳税总额":
                    report.tax_total = string.IsNullOrEmpty(reportDetail.nsze) ? "" : reportDetail.nsze.Replace(" ", "");
                    // "所有者权益合计":
                    report.profit_reta = string.IsNullOrEmpty(reportDetail.qyhj) ? "" : reportDetail.qyhj.Replace(" ", "");
                }
            }
        }
        #endregion

        #region 年报修改记录
        void LoadAndParseReportUpdateRecord(string responseData, Report report)
        {
            ReportUpdateRecordGUIZHOUGT reportGZ = JsonConvert.DeserializeObject<ReportUpdateRecordGUIZHOUGT>(responseData);

            if (reportGZ.data != null && reportGZ.data.Length > 0)
            {
                foreach (var itemJson in reportGZ.data)
                {
                    UpdateRecord item = new UpdateRecord();

                    item.seq_no = itemJson.rownum;
                    item.update_item = itemJson.bgsxmc;
                    item.before_update = itemJson.bgq;
                    item.after_update = itemJson.bgh;
                    item.update_date=itemJson.bgrq;
                    report.update_records.Add(item);
                }
            }
        }
        #endregion

        #region 其他部门公示信息-行政处罚
        void LoadAndParseXZCF_Other(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            XingZhengChuFaJsonGUIZHOU xzcf = JsonConvert.DeserializeObject<XingZhengChuFaJsonGUIZHOU>(responseData);
            if (xzcf != null && xzcf.successed && xzcf.data.Any())
            {
                foreach (var item in xzcf.data)
                {
                    AdministrativePunishment ap = new AdministrativePunishment()
                    {
                        seq_no = item.rownum,
                        number = item.cfjdsh == null ? "" : item.cfjdsh,
                        illegal_type = item.wfxwlx,
                        content = item.xzcfnr,
                        department = item.cfjg,
                        date = item.cfrq,
                        name = _enterpriseInfo.name,
                        reg_no = _enterpriseInfo.reg_no,
                        oper_name = _enterpriseInfo.oper_name,
                         public_date = item.gsrq
                    };

                    this.LoadAndParseXZCF_Other_Detail(ap,item.gsbh);
                    //gsbh
                    _enterpriseInfo.administrative_punishments.Add(ap);
                }
            }
        }

        void LoadAndParseXZCF_Other_Detail(AdministrativePunishment ap,string gsbh)
        {
            var request = this.CreateRequest();
            request.AddOrUpdateRequestParameter("gsbh",gsbh);
            var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("other_xingzhengchufa_detail"));
            if (responseList != null && responseList.Any())
            {
                XingZhengChuFaJsonGUIZHOUDetail detail = JsonConvert.DeserializeObject<XingZhengChuFaJsonGUIZHOUDetail>(responseList.First().Data);
                if (detail.successed && detail.gsxx != null)
                {
                    ap.reg_no = detail.gsxx.zch;
                    ap.oper_name = detail.gsxx.fddbr;
                    ap.description = detail.gsxx.cfnr;
                }
            }
        }
        #endregion

        #region 股权出资信息
        private void LoadAndParseGuQuanChuZi(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            GuquanchuziJsonGUIZHOU gqcz = JsonConvert.DeserializeObject<GuquanchuziJsonGUIZHOU>(responseData);
            if (gqcz != null && gqcz.successed && gqcz.data.Any())
            {
                var c = 1;
                foreach (var item in gqcz.data)
                {

                    EquityQuality equityquality = new EquityQuality()
                    {
                        seq_no = item.rownum,
                        number = item.djbh,
                        pledgor = item.czr,
                        pledgor_identify_no = item.czzjhm,
                        pledgor_amount = item.czgqse,
                        pawnee = item.zqr,
                        pawnee_identify_no = item.zqzjhm,
                        date = item.czrq,
                        status = item.zt,
                         public_date = item.gsrq
                    };
                    _enterpriseInfo.equity_qualities.Add(equityquality);
                    c++;
                }
            }

        }
        #endregion
        private string convertCash(string inText)
        {
            string result = "";
            if (inText != null) {
                result = inText.Replace(" ", "");
                if ("万元人民币"==result) {
                    result = "";
                }
            }
            return result;
        }
    }
}