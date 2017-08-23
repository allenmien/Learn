using System;
using System.IO;
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

namespace iOubo.iSpider.Infrastructure.Parser
{
    public class ConverterQG : IConverter
    {
        DataRequest _request;
        RequestInfo _requestInfo;
        RequestXml _requestXml;
        EnterpriseInfo _enterpriseInfo = new EnterpriseInfo();
        List<AbnormalInfo> _abnormals = new List<AbnormalInfo>();
        List<CheckupInfo> _checkups = new List<CheckupInfo>();
        List<string> reportsNeedToLoad = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings.Get("ReportsNeedToLoad"))
            ? new List<string>() : ConfigurationManager.AppSettings.Get("ReportsNeedToLoad").Split(',').ToList();
        Dictionary<string, string> imgAddr = new Dictionary<string, string>();
        BsonDocument document = new BsonDocument();
        string _entType = string.Empty;
        public RequestHandler request = new RequestHandler();
        string branchesId = string.Empty;
        public SummaryEntity ProcessRequestAndParse(RequestInfo requestInfo)
        {
            this._requestInfo = requestInfo;
            this._request = new DataRequest(requestInfo);
            this._requestXml = new RequestXml(requestInfo.CurrentPath, "QG");
            InitialEnterpriseInfo();
            //解析基本信息
            List<XElement> requestList = null;
            requestList = _requestXml.GetRequestListByName("basic").ToList();
            List<ResponseInfo> responseList = _request.GetResponseInfo(requestList);
            this.LoadAndPaseBasicInfo(responseList[0].Data);

            if (_entType == "16")
            {
                requestList = _requestXml.GetRequestListByGroup("gt").ToList();
                requestList.AddRange(_requestXml.GetRequestListByGroup("genera"));
            }
            else if (_entType == "17")
            {
                requestList = _requestXml.GetRequestListByGroup("qy").ToList();
                requestList = _requestXml.GetRequestListByGroup("hzs").ToList();
                requestList.AddRange(_requestXml.GetRequestListByGroup("gengera"));
            }
            else
            {
                requestList = _requestXml.GetRequestListByGroup("qy").ToList();
                requestList.AddRange(_requestXml.GetRequestListByGroup("gengera"));
            }
            responseList = this.GetResponseInfo(requestList);
            foreach (ResponseInfo responseInfo in responseList)
            {
                this.LoadData(responseInfo);
            }
            SummaryEntity summaryEntity = new SummaryEntity();

            #region 省份匹配
            var no = "";
            if (_enterpriseInfo.credit_no != null && _enterpriseInfo.credit_no.Length == 18)
            {
                no = _enterpriseInfo.credit_no.Substring(2, 2);
            }
            else if (_enterpriseInfo.province != null && _enterpriseInfo.reg_no.Length < 18)
            {
                no = _enterpriseInfo.reg_no.Substring(0, 2);
            }
            var resu = MatchPro(no);

            _enterpriseInfo.province = resu;
            #endregion
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

        
        void LoadData(ResponseInfo item)
        {
            switch (item.Name)
            {
                
                case "partner":
                    LoadAndParsePartnersList(item.Data);
                    break;
                case "employee":
                    LoadAndParseStaffs(item.Data);
                    break;
                case "alterInfo":
                    LoadAndParseChangeRecordsList(item.Data);
                    break;
                case "branch":
                   LoadAndParseBranches(item.Data);
                    break;
                case "motage":
                    LoadAndParseMortgageList(item.Data);
                    break;
                case "xzxk1":
                    LoadAndParseLicenseList(item.Data,1);
                    break;
                case "xzxk2":
                    LoadAndParseLicenseList(item.Data, 2);
                    break;
                case "gqbg":
                    LoadAndParseStockChangesList(item.Data);
                    break;
                case "abnormal":
                    LoadAndParseAbnormalList(item.Data);
                    break;
                case "check":
                    LoadAndParseCheckUpList(item.Data);
                    break;
                case "report":
                    LoadAndParseReports(item.Data);
                    break;
                case "gqcz":
                    LoadAndParseEquityQualityList(item.Data);
                    break;
                case "sfxz":
                    LoadAndParseFreezeList(item.Data);
                    break;
                case "xzcf1":
                    LoadAndParsePunishmentsList(item.Data,1);                    
                    break;
                case "xzcf2":
                    LoadAndParsePunishmentsList(item.Data, 2);
                    break;
                case "zscq1":
                    LoadAndParsePledgeRegList(item.Data, 1);
                    break;
                case "zscq2":
                    LoadAndParsePledgeRegList(item.Data, 2);
                    break;
                case "investor":
                    LoadAndParseFinancialList(item.Data);
                    break;
            }
        }
        #region 知识产权List
        private void LoadAndParsePledgeRegList(string response, int flag)
        {
            var zscq = flag == 1 ? "zscq1" : "zscq2";

            LoadAndParsePledgeReg(response);
            QGZscqList cqInfo = JsonConvert.DeserializeObject<QGZscqList>(response);
            if (cqInfo.totalPage > 1)
            {
                for (int i = 2; i <= cqInfo.totalPage; i++)
                {
                    _request.AddOrUpdateRequestParameter("pageno", i.ToString());
                    _request.AddOrUpdateRequestParameter("start", ((i - 1) * 5).ToString());
                    List<XElement> requestList = _requestXml.GetRequestListByGroup(zscq).ToList();
                    List<ResponseInfo> responseList = GetResponseInfo(requestList);
                    LoadAndParsePledgeReg(responseList[0].Data);
                }
            }
            
        }
        #endregion

        #region 知识产权
        private void LoadAndParsePledgeReg(string response)
        {
            QGZscqList cqInfo = JsonConvert.DeserializeObject<QGZscqList>(response);
            if(cqInfo!=null && cqInfo.data!=null && cqInfo.data.Length>0)
            {
                for(int i=0;i<cqInfo.data.Length;i++)
                {
                    QGZscqInfo item = cqInfo.data[i];
                    Utility.ClearNullValue<QGZscqInfo>(item);
                    KnowledgeProperty zscq = new KnowledgeProperty();
                    zscq.seq_no = _enterpriseInfo.knowledge_properties.Count + 1;
                    zscq.number = item.tmRegNo;
                    zscq.name = item.tmName;
                    zscq.type = item.kinds;
                    zscq.pledgor = item.pledgor;
                    zscq.pawnee = item.impOrg;
                    var startdate = string.IsNullOrEmpty(item.pleRegPerFrom) ? string.Empty : ConvertStringToDate(item.pleRegPerFrom);
                    var enddate = string.IsNullOrEmpty(item.pleRegPerTo) ? string.Empty : ConvertStringToDate(item.pleRegPerTo);
                    zscq.public_date = string.IsNullOrEmpty(item.publicDate) ? string.Empty : ConvertStringToDate(item.publicDate);
                    zscq.period = startdate + "至" + enddate;
                    if (zscq.period == "至")
                    {
                        zscq.period = "";
                    }
                    zscq.status = item.type == "1" ? "有效" : "无效";
                    _enterpriseInfo.knowledge_properties.Add(zscq);
                }

            }
        }
        #endregion

        #region 股东出资List
        private void LoadAndParseFinancialList(string response)
        {
            List<FinancialContribution> list = new List<FinancialContribution>();
            LoadAndParseFinancialitem(response, list);
            QGinvestorList cqInfo = JsonConvert.DeserializeObject<QGinvestorList>(response);
            if (cqInfo.totalPage > 1)
            {
                for (int i = 2; i <= cqInfo.totalPage; i++)
                {
                    _request.AddOrUpdateRequestParameter("pageno", i.ToString());
                    _request.AddOrUpdateRequestParameter("start", ((i - 1) * 5).ToString());
                    List<XElement> requestList = _requestXml.GetRequestListByGroup("investor").ToList();
                    List<ResponseInfo> responseList = GetResponseInfo(requestList);
                    LoadAndParseFinancialitem(responseList[0].Data, list);
                }
            }
            _enterpriseInfo.financial_contributions = list;

        }
        #endregion
        private void LoadAndParseFinancialitem(string response,List<FinancialContribution> list)
        {

            QGinvestorList cqInfo = JsonConvert.DeserializeObject<QGinvestorList>(response);
            if (cqInfo != null && cqInfo.data.Length>0)
            {
                for (int i = 0; i < cqInfo.data.Length; i++)
                {
                    QGinvestorInfo item = cqInfo.data[i];
                    Utility.ClearNullValue<QGinvestorInfo>(item);
                    FinancialContribution financialcontribution = new FinancialContribution();
                    financialcontribution.seq_no = list.Count + 1;
                    financialcontribution.investor_name = item.inv;
                    financialcontribution.total_real_capi = string.IsNullOrEmpty(item.aubSum) ? "" : item.aubSum+"万元";
                    financialcontribution.total_should_capi = string.IsNullOrEmpty(item.subSum) ? "" : item.subSum + "万元";
                    List<FinancialContribution.ShouldCapiItem> should_capi_items = new List<FinancialContribution.ShouldCapiItem>();
                    List<FinancialContribution.RealCapiItem> real_capi_items = new List<FinancialContribution.RealCapiItem>();

                    if (item.subDetails != null && item.subDetails.Length > 0)
                    {
                        foreach (QGSubDetail subItem in item.subDetails)
                        {
                            FinancialContribution.ShouldCapiItem CapiItem = new FinancialContribution.ShouldCapiItem();
                            CapiItem.should_invest_type = subItem.subConForm_CN;
                            CapiItem.should_capi = string.IsNullOrEmpty(subItem.subConAm)?"":subItem.subConAm+"万元";
                            CapiItem.should_invest_date = string.IsNullOrEmpty(subItem.currency) ? string.Empty : ConvertStringToDate(subItem.currency);
                            CapiItem.public_date = string.Empty;
                            should_capi_items.Add(CapiItem);
                        }
                        financialcontribution.should_capi_items = should_capi_items;
                    }
                    if (item.aubDetails != null && item.aubDetails.Length > 0)
                    {
                        foreach (QGAubDetail acItem in item.aubDetails)
                        {
                            Utility.ClearNullValue<QGAubDetail>(acItem);
                            FinancialContribution.RealCapiItem ReCapiItem = new FinancialContribution.RealCapiItem();
                            ReCapiItem.real_invest_type = acItem.acConFormName;
                            ReCapiItem.real_capi = string.IsNullOrEmpty(acItem.acConAm) ? "" : acItem.acConAm + "万元";
                            ReCapiItem.real_invest_date = string.IsNullOrEmpty(acItem.conDate) ? string.Empty : ConvertStringToDate(acItem.conDate);
                            ReCapiItem.public_date = string.Empty;
                            real_capi_items.Add(ReCapiItem);
                        }
                        financialcontribution.real_capi_items = real_capi_items;
                    }

                    list.Add(financialcontribution);
                }
            }

        }

        #region 行政处罚List
        private void LoadAndParsePunishmentsList(string response,int flag)
        {
            var xzcf = string.Empty;
            if(flag==1)
            {
                xzcf = "xzcf1";
            }
            else if(flag==2)
            {
                xzcf = "xzcf2";
            }
           // List<AdministrativePunishment> punishmentList = new List<AdministrativePunishment>();
            LoadAndParsePunishments(response);
            QGXzcfList cqInfo = JsonConvert.DeserializeObject<QGXzcfList>(response);
            if (cqInfo.totalPage > 1)
            {
                for (int i = 2; i <= cqInfo.totalPage; i++)
                {
                    _request.AddOrUpdateRequestParameter("pageno", i.ToString());
                    _request.AddOrUpdateRequestParameter("start", ((i - 1) * 5).ToString());
                    List<XElement> requestList = _requestXml.GetRequestListByGroup(xzcf).ToList();
                    List<ResponseInfo> responseList = GetResponseInfo(requestList);
                    LoadAndParsePunishments(responseList[0].Data);
                }
            }           

        }
        #endregion
        private void LoadAndParsePunishments(string response)
        {

            #region 行政处罚信息信息
            // 行政处罚信息信息
            
            QGXzcfList info = JsonConvert.DeserializeObject<QGXzcfList>(response);
            if (info.data != null && info.data.Length > 0)
            {
                for (int i = 0; i < info.data.Length;i++ )
                {
                    QGXzcfInfo item = info.data[i];
                    Utility.ClearNullValue<QGXzcfInfo>(item);
                    AdministrativePunishment punish = new AdministrativePunishment();
                    punish.seq_no = _enterpriseInfo.administrative_punishments.Count + 1;
                    punish.number = item.penDecNo;
                    punish.date = string.IsNullOrEmpty(item.penDecIssDate)?string.Empty:ConvertStringToDate(item.penDecIssDate);
                    punish.department = item.penAuth_CN;
                    punish.illegal_type = item.illegActType;
                    punish.content = string.IsNullOrEmpty(item.penContent) ? item.penType_CN : item.penContent;
                    punish.name = _enterpriseInfo.name;
                    punish.oper_name = _enterpriseInfo.oper_name;
                    punish.reg_no = _enterpriseInfo.reg_no;
                    punish.public_date = string.IsNullOrEmpty(item.publicDate) ? string.Empty : ConvertStringToDate(item.publicDate);
                    _enterpriseInfo.administrative_punishments.Add(punish);
                }               
            }
           
            #endregion
        }

        #region 股权冻结List
        private void LoadAndParseFreezeList(string response)
        {
            List<JudicialFreeze> freezes = new List<JudicialFreeze>();
            LoadAndParseFreeze(response, freezes);
            QGSfxzList cqInfo = JsonConvert.DeserializeObject<QGSfxzList>(response);
            if (cqInfo.totalPage > 1)
            {
                for (int i = 2; i <= cqInfo.totalPage; i++)
                {
                    _request.AddOrUpdateRequestParameter("pageno", i.ToString());
                    _request.AddOrUpdateRequestParameter("start", ((i - 1) * 5).ToString());
                    List<XElement> requestList = _requestXml.GetRequestListByGroup("sfxz").ToList();
                    List<ResponseInfo> responseList = GetResponseInfo(requestList);
                    LoadAndParseFreeze(responseList[0].Data, freezes);
                }
            }
            _enterpriseInfo.judicial_freezes = freezes;

        }
        #endregion

        #region 股权冻结
        private void LoadAndParseFreeze(string responseData, List<JudicialFreeze> freezes)
        {

            QGSfxzList list = JsonConvert.DeserializeObject<QGSfxzList>(responseData);           

            if (list != null && list.data != null && list.data.Length>0)
            {
                foreach (QGSfxzInfo item in list.data)
                {
                    Utility.ClearNullValue<QGSfxzInfo>(item);
                    JudicialFreeze freeze = new JudicialFreeze();
                    freeze.seq_no = freezes.Count + 1;
                    freeze.be_executed_person = item.inv;
                    freeze.amount = string.IsNullOrEmpty(item.froAm) ? "" : item.froAm+"万"+item.regCapCur_CN;
                    freeze.executive_court = item.froAuth;
                    freeze.number = string.IsNullOrEmpty(item.executeNo) ? string.Empty : item.executeNo;
                    freeze.status = item.frozState == "1" ? "股权冻结|冻结" : item.frozState == "2" ? "股权冻结|解除冻结" : item.frozState == "3"?"股权冻结|失效":"股权变更";
                    _request.AddOrUpdateRequestParameter("DetailFreezeId", item.parent_Id);
                    List<XElement> requestList = null;
                    List<ResponseInfo> responseList = null;
                    requestList = _requestXml.GetRequestListByGroup("freeze").ToList();
                    responseList = GetResponseInfo(requestList);
                    QGSfxzList free = JsonConvert.DeserializeObject<QGSfxzList>(responseList[0].Data);
                    #region 详情
                    JudicialFreezeDetail detail = new JudicialFreezeDetail();
                    if (free.data != null && free.data.Length>0)
                    {
                        QGSfxzInfo gqdj = free.data[0];
                        Utility.ClearNullValue<QGSfxzInfo>(gqdj);
                        detail.adjudicate_no = gqdj.froDocNo;
                        detail.execute_court = gqdj.froAuth;
                        detail.assist_name = gqdj.inv;
                        detail.assist_item = gqdj.executeItem_CN;
                        detail.assist_ident_type = string.IsNullOrEmpty(gqdj.cerType_CN) ? string.Empty : gqdj.cerType_CN;
                        detail.assist_ident_no = string.IsNullOrEmpty(gqdj.cerNo) ? string.Empty : gqdj.cerNo;
                        detail.freeze_start_date = string.IsNullOrEmpty(gqdj.froFrom) ? "" : ConvertStringToDate(gqdj.froFrom);
                        detail.freeze_end_date = string.IsNullOrEmpty(gqdj.froTo) ? "" : ConvertStringToDate(gqdj.froTo);
                        detail.freeze_year_month = gqdj.frozDeadline;
                        detail.freeze_amount = gqdj.froAm;
                        detail.notice_no = string.IsNullOrEmpty(gqdj.executeNo) ? string.Empty : gqdj.executeNo;
                        detail.public_date = string.IsNullOrEmpty(gqdj.publicDate) ? "" : ConvertStringToDate(gqdj.publicDate) ;
                        freeze.detail = detail;
                    }
                    //if (gqjd != null)
                    //{
                    //    Utility.ClearNullValue<Gqjd>(gqjd);
                    //    JudicialUnFreezeDetail un_freeze_detail = new JudicialUnFreezeDetail();
                    //    //Utility.ClearNullValue<JudicialUnFreezeDetail>(un_freeze_detail);
                    //    un_freeze_detail.adjudicate_no = gqjd.frodocno;
                    //    un_freeze_detail.execute_court = gqjd.froauth;
                    //    un_freeze_detail.assist_name = gqjd.inv;
                    //    un_freeze_detail.assist_item = "解除冻结股权、其他投资权益";
                    //    un_freeze_detail.assist_ident_type = string.IsNullOrEmpty(gqjd.certype) ? string.Empty : gqjd.certype;
                    //    un_freeze_detail.assist_ident_no = string.IsNullOrEmpty(gqjd.cerno) ? string.Empty : gqjd.cerno;
                    //    un_freeze_detail.unfreeze_date = gqjd.thawdate;
                    //    un_freeze_detail.freeze_amount = gqjd.froam;
                    //    un_freeze_detail.notice_no = string.IsNullOrEmpty(gqdj.executeno) ? string.Empty : gqdj.executeno;
                    //    un_freeze_detail.public_date = gqjd.publicdate;
                    //    freeze.un_freeze_detail = un_freeze_detail;
                    //}
                    #endregion
                    freezes.Add(freeze);
                }
            }            
        }
        #endregion


        #region 个体年报信息详情
        private void LoadAndParseGTReportsDetail(List<ResponseInfo> responseList, Report report)
        {
           
            //基本信息
            foreach (ResponseInfo responseinfo in responseList)
            {
                if (responseinfo.Name == "base")
                {
                    var content = responseinfo.Data;
                    QGGtReportBaseInfo cqre = JsonConvert.DeserializeObject<QGGtReportBaseInfo>(content);
                    if (cqre != null)
                    {
                        //CQReportBaseInfo reportDetail = cqre.form;
                        Utility.ClearNullValue<QGGtReportBaseInfo>(cqre);
                        report.name = cqre.traName;
                        report.oper_name = cqre.name;
                        cqre.regNo = cqre.regNo.Replace("\0", "");
                        cqre.uniscId = cqre.uniscId.Replace("\0", "");
                        if (cqre.uniscId != null && cqre.uniscId.Length == 15)
                            report.reg_no = cqre.uniscId;
                        if (cqre.uniscId != null && cqre.uniscId.Length == 18)
                            report.credit_no = cqre.uniscId;
                        if (cqre.regNo != null && cqre.regNo.Length == 15)
                            report.reg_no = cqre.regNo;
                        if (cqre.regNo != null && cqre.regNo.Length == 18)
                            report.credit_no = cqre.regNo;
                        report.telephone = cqre.tel;
                        report.reg_capi = string.IsNullOrEmpty(cqre.fundAm) ? string.Empty : cqre.fundAm + "万";
                        report.if_website = "否";
                        report.collegues_num = cqre.empNumDis == "1" ? cqre.empNum + "人" : "个体户选择不公示";
                        report.sale_income = cqre.vendIncDis == "1" ? cqre.vendInc + "万元" : "个体户选择不公示";
                        report.tax_total = cqre.ratGroDis == "1" ? cqre.ratGro + "万元" : "个体户选择不公示";
                    }
                }
                else if (responseinfo.Name == "website")
                {
                    var content = responseinfo.Data;
                    QGReportWebsiteList cqre = JsonConvert.DeserializeObject<QGReportWebsiteList>(content);
                    if (cqre.data != null && cqre.data.Length > 0)
                    {
                        report.if_website = "是";
                        List<WebsiteItem> websiteList = new List<WebsiteItem>();
                        for (int i = 0; i < cqre.data.Length; i++)
                        {
                            QGReportWebsiteInfo itemJson = cqre.data[i];
                            Utility.ClearNullValue<QGReportWebsiteInfo>(itemJson);
                            WebsiteItem item = new WebsiteItem();
                            item.seq_no = i + 1;
                            item.web_type = itemJson.webType == "1" ? "网站" : "网店";
                            item.web_name = itemJson.webSitName;
                            item.web_url = itemJson.domain;
                            // item.date = string.IsNullOrEmpty(itemJson.datainstime) ? "" : itemJson.datainstime.Split(' ')[0];
                            websiteList.Add(item);
                        }
                        report.websites = websiteList;
                    }
                }
                else if (responseinfo.Name == "alter")
                {
                    var content = responseinfo.Data;
                    //document = BsonDocument.Parse(content);
                    QGReportAlterList cqre = JsonConvert.DeserializeObject<QGReportAlterList>(content);
                    List<UpdateRecord> records = new List<UpdateRecord>();
                    if (cqre.totalPage != null && cqre.totalPage > 0)
                    {

                        for (int i = 1; i <= cqre.totalPage; i++)
                        {
                            List<RequestSetting> list = new List<RequestSetting>();
                            list.Add(new RequestSetting
                            {
                                Method = "post",
                                Url = "http://www.gsxt.gov.cn" + document["alterUrl"] + "?entType=" + _entType,
                                IsArray = "0",
                                Name = "alter",
                                Data = string.Format("draw={0}&start={1}&length=5", i.ToString(), ((i - 1) * 5).ToString())
                            });
                            var responsel = _request.GetResponseInfo(list);
                            cqre = JsonConvert.DeserializeObject<QGReportAlterList>(responsel[0].Data);
                            if (cqre.data != null && cqre.data.Length > 0)
                            {
                                for (int j = 0; j < cqre.data.Length; j++)
                                {
                                    QGReportAlterInfo itemJson = cqre.data[j];
                                    Utility.ClearNullValue<QGReportAlterInfo>(itemJson);
                                    UpdateRecord item = new UpdateRecord();
                                    item.seq_no = records.Count + 1;
                                    item.update_item = itemJson.alitem;
                                    item.before_update = itemJson.altBe;
                                    item.after_update = itemJson.altAf;
                                    item.update_date = string.IsNullOrEmpty(itemJson.altDate) ? string.Empty : ConvertStringToDate(itemJson.altDate);
                                    records.Add(item);
                                }
                                report.update_records = records;
                            }
                        }
                    }

                }
                  

                  //  }

                else if (responseinfo.Name == "base2")
                {
                    var content = responseinfo.Data;
                    document = BsonDocument.Parse(content);
                    //  var totalPage = document.Contains("totalPage") ? (document["totalPage"].IsBsonNull ? 1 : document["totalPage"].AsInt32) : 1;
                    List<RequestSetting> list = new List<RequestSetting>();
                    list.Add(new RequestSetting
                    {
                        Method = "get",
                        Url = "http://www.gsxt.gov.cn" + document["alterUrl"] + "?entType=" + _entType,
                        IsArray = "0",
                        Name = "alter",
                        //Data = "draw=1&start=0&length=5",
                    });
                    list.Add(new RequestSetting
                    {
                        Method = "get",
                        Url = "http://www.gsxt.gov.cn" + document["webSiteInfoUrl"] + "?entType=" + _entType,
                        IsArray = "0",
                        Name = "website",
                        //Data = string.Format("draw={0}&start={1}&length=5", i.ToString(), ((i - 1) * 5).ToString())
                    });
                    var responsel = _request.GetResponseInfo(list);
                    if (responsel != null && responseList.Any())
                    {
                        LoadAndParseGTReportsDetail(responsel, report);
                    }
                }
            }
        }
        #endregion

        #region 解析reportlist

        private void LoadAndParseReports(string response)
        {
            //response = response.Replace("[{\"total", "{\"total").Replace("]}]", "]}");
            QGReportList[] cqReqort = JsonConvert.DeserializeObject<QGReportList[]>(response);
            List<Report> reportList = new List<Report>();
            if (cqReqort != null && cqReqort.Length > 0)
            {
                for (int i = 0; i < cqReqort.Length; i++)
                {
                    QGReportList item = cqReqort[i];
                    Utility.ClearNullValue<QGReportList>(item);
                    Report re = new Report();
                    re.report_year = item.anCheYear;
                    re.report_date = String.IsNullOrEmpty(item.anCheDate) ? "" : ConvertStringToDate(item.anCheDate);
                    if ((item.annRepFrom != null && item.annRepFrom != "2") || (item.entType != null && item.entType != "16"))
                    {
                        _request.AddOrUpdateRequestParameter("ancheid", item.anCheId);
                        List<XElement> requestList = null;
                        List<ResponseInfo> responseList = null;
                        if (item.entType == "pb")
                        {
                            requestList = _requestXml.GetRequestListByGroup("gtreport").ToList();
                            responseList = GetResponseInfo(requestList);
                            if (responseList != null && responseList.Any())
                            {
                                this.LoadAndParseGTReportsDetail(responseList, re);
                                reportList.Add(re);
                            }
                        }
                        else if (item.entType == "sfc")
                        {
                            //requestList = _requestXml.GetRequestListByGroup("hzsreport").ToList();
                            //responseList = GetResponseInfo(requestList);
                            //if (responseList != null && responseList.Any())
                            //{
                            //    this.LoadAndParseReportsDetail(responseList, re);
                                reportList.Add(re);
                            //}
                        }
                        else
                        {
                            requestList = _requestXml.GetRequestListByGroup("report").ToList();
                            responseList = GetResponseInfo(requestList);
                            if (responseList != null && responseList.Any())
                            {
                                this.LoadAndParseReportsDetail(responseList, re);
                                reportList.Add(re);
                            }
                        }

                    }
                }
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

        #region 解析年报详细页面
        private void LoadAndParseReportsDetail(List<ResponseInfo> responseList, Report report)
        {

            //基本信息
            foreach (ResponseInfo responseinfo in responseList)
            {
                if (responseinfo.Name == "base")
                {
                    var content = responseinfo.Data;
                    QGReportList cqre = JsonConvert.DeserializeObject<QGReportList>(content);
                    if (cqre!= null)
                    {
                        //CQReportBaseInfo reportDetail = cqre.form;
                        Utility.ClearNullValue<QGReportList>(cqre);
                        report.name = cqre.entName;
                        cqre.regNo = cqre.regNo.Replace("\0", "");
                        cqre.uniscId = cqre.uniscId.Replace("\0", "");
                        if (cqre.uniscId != null && cqre.uniscId.Length == 15)
                            report.reg_no = cqre.uniscId;
                        if (cqre.uniscId != null && cqre.uniscId.Length == 18)
                            report.credit_no = cqre.uniscId;
                        if (cqre.regNo != null && cqre.regNo.Length == 15)
                            report.reg_no = cqre.regNo;
                        if (cqre.regNo != null && cqre.regNo.Length == 18)
                            report.credit_no = cqre.regNo;
                        report.reg_no = cqre.regNo;
                        report.telephone = cqre.tel;
                        report.address = cqre.addr;
                        report.zip_code = cqre.postalCode;
                        report.email = cqre.email;
                        report.if_external_guarantee = "否";
                        report.if_equity = "否";
                        report.if_invest = "否";
                        report.if_website = "否";
                        report.status = cqre.busSt_CN;
                        report.collegues_num = cqre.empNumDis=="1"?cqre.empNum+"人":"企业选择不公示";
                        //report.if_external_guarantee = reportDetail.hasexternalsecurity == "1" ? "是" : "否";
                        //report.if_invest = reportDetail.maibusincdis == "1" ? "是" : "否";
                        //report.if_equity = reportDetail.istransfer == "1" ? "是" : "否";
                        report.total_equity = cqre.assGroDis == "1" ? cqre.assGroDis + "万元" : "企业选择不公示";
                          //  string.IsNullOrEmpty(reportDetail.assgro) ? "企业选择不公示" : reportDetail.assgro == "企业选择不公示" ? reportDetail.assgro : reportDetail.assgro + "万元人民币";
                        report.sale_income = cqre.vendIncDis == "1" ? cqre.vendInc + "万元" : "企业选择不公示";
                            //string.IsNullOrEmpty(reportDetail.vendinc) ? "企业选择不公示" : reportDetail.vendinc == "企业选择不公示" ? reportDetail.vendinc : reportDetail.vendinc + "万元人民币";
                        report.serv_fare_income = cqre.maiBusIncDis == "1" ? cqre.maiBusInc + "万元" : "企业选择不公示";
                            //string.IsNullOrEmpty(reportDetail.maibusinc) ? "企业选择不公示" : reportDetail.maibusinc == "企业选择不公示" ? reportDetail.maibusinc : reportDetail.maibusinc + "万元人民币";
                        report.tax_total = cqre.ratGroDis == "1" ? cqre.ratGro + "万元" : "企业选择不公示";
                            //string.IsNullOrEmpty(reportDetail.ratgro) ? "企业选择不公示" : reportDetail.ratgro == "企业选择不公示" ? reportDetail.ratgro : reportDetail.ratgro + "万元人民币";
                        report.profit_reta = cqre.totEquDis == "1" ? cqre.totEqu + "万元" : "企业选择不公示";
                            //string.IsNullOrEmpty(reportDetail.totequ) ? "企业选择不公示" : reportDetail.totequ == "企业选择不公示" ? reportDetail.totequ : reportDetail.totequ + "万元人民币";
                        report.profit_total = cqre.proGroDis == "1" ? cqre.proGro + "万元" : "企业选择不公示";
                            //string.IsNullOrEmpty(reportDetail.progro) ? "企业选择不公示" : reportDetail.progro == "企业选择不公示" ? reportDetail.progro : reportDetail.progro + "万元人民币";
                        report.net_amount = cqre.netIncDis == "1" ? cqre.netInc + "万元" : "企业选择不公示";
                            //string.IsNullOrEmpty(reportDetail.netinc) ? "企业选择不公示" : reportDetail.netinc == "企业选择不公示" ? reportDetail.netinc : reportDetail.netinc + "万元人民币";
                        report.debit_amount = cqre.liaGroDis == "1" ? cqre.liaGro + "万元" : "企业选择不公示";
                            //string.IsNullOrEmpty(reportDetail.liagro) ? "企业选择不公示" : reportDetail.liagro == "企业选择不公示" ? reportDetail.liagro : reportDetail.liagro + "万元人民币";

                    }
                }
                else if (responseinfo.Name == "website")
                {
                    var content = responseinfo.Data;
                    QGReportWebsiteList cqre = JsonConvert.DeserializeObject<QGReportWebsiteList>(content);
                    if (cqre.data != null && cqre.data.Length > 0)
                    {
                        report.if_website = "是";
                        List<WebsiteItem> websiteList = new List<WebsiteItem>();
                        for (int i = 0; i < cqre.data.Length; i++)
                        {
                            QGReportWebsiteInfo itemJson = cqre.data[i];
                            Utility.ClearNullValue<QGReportWebsiteInfo>(itemJson);
                            WebsiteItem item = new WebsiteItem();
                            item.seq_no = i + 1;
                            item.web_type = itemJson.webType=="1"?"网站":"网店";
                            item.web_name = itemJson.webSitName;
                            item.web_url = itemJson.domain;
                           // item.date = string.IsNullOrEmpty(itemJson.datainstime) ? "" : itemJson.datainstime.Split(' ')[0];
                            websiteList.Add(item);
                        }
                        report.websites = websiteList;
                    }
                }
                // 股东及出资
                else if (responseinfo.Name == "captial")
                {
                    var content = responseinfo.Data;
                    QGReportGdczList cqre = JsonConvert.DeserializeObject<QGReportGdczList>(content);
                    if (cqre.totalPage != null && cqre.totalPage > 0)
                    {
                        List<Partner> partnerList = new List<Partner>();
                        for (int i = 1; i <= cqre.totalPage; i++)
                        {
                            if (i > 1)
                            {
                                _request.AddOrUpdateRequestParameter("pageno", i.ToString());
                                _request.AddOrUpdateRequestParameter("start", ((i - 1) * 5).ToString());
                                List<XElement> requestList = _requestXml.GetRequestListByGroup("reportcaptial").ToList();
                                List<ResponseInfo> response = GetResponseInfo(requestList);
                                cqre = JsonConvert.DeserializeObject<QGReportGdczList>(response[0].Data);
                            }

                            if (cqre.data != null && cqre.data.Length > 0)
                            {
                                
                                for (int j = 0; j < cqre.data.Length; j++)
                                {
                                    QGReportGdczInfo itemJson = cqre.data[j];
                                    Utility.ClearNullValue<QGReportGdczInfo>(itemJson);
                                    Partner item = new Partner();
                                    item.seq_no = partnerList.Count+1;
                                    item.stock_name = itemJson.invName;
                                    
                                    item.identify_no = "";
                                    item.identify_type = "";
                                    item.stock_percent = "";
                                    item.ex_id = "";
                                    item.should_capi_items = new List<ShouldCapiItem>();
                                    item.real_capi_items = new List<RealCapiItem>();

                                    ShouldCapiItem sItem = new ShouldCapiItem();
                                    sItem.shoud_capi = string.IsNullOrEmpty(itemJson.liSubConAm) ? "" : itemJson.liSubConAm + "万元";
                                    sItem.should_capi_date = string.IsNullOrEmpty(itemJson.subConDate) ? "" : ConvertStringToDate(itemJson.subConDate);
                                    sItem.invest_type = itemJson.subConFormName;
                                    if (sItem.shoud_capi != null && sItem.shoud_capi.Length > 0)
                                    {
                                        item.should_capi_items.Add(sItem);
                                    }
                                    RealCapiItem rItem = new RealCapiItem();
                                    rItem.real_capi = string.IsNullOrEmpty(itemJson.liAcConAm) ? "" : itemJson.liAcConAm + "万元";
                                    rItem.real_capi_date = string.IsNullOrEmpty(itemJson.acConDate) ? "" : ConvertStringToDate(itemJson.acConDate);
                                    rItem.invest_type = itemJson.acConForm_CN;
                                    if (rItem.real_capi != null && rItem.real_capi.Length > 0)
                                    {
                                        item.real_capi_items.Add(rItem);
                                    }
                                    partnerList.Add(item);
                                }                               
                            }
                        }
                        report.partners = partnerList;
                    }
                }
                //对外担保
                else if (responseinfo.Name == "dwdb")
                {
                    var content = responseinfo.Data;
                    QGReportDwdbList cqre = JsonConvert.DeserializeObject<QGReportDwdbList>(content);
                    if (cqre.totalPage != null && cqre.totalPage > 0)
                    {
                        report.if_external_guarantee = "是";
                        List<ExternalGuarantee> guarantee_items = new List<ExternalGuarantee>();
                        for (int i = 1; i <= cqre.totalPage; i++)
                        {
                            if (i > 1)
                            {
                                _request.AddOrUpdateRequestParameter("pageno", i.ToString());
                                _request.AddOrUpdateRequestParameter("start", ((i - 1) * 5).ToString());
                                List<XElement> requestList = _requestXml.GetRequestListByGroup("reportdwdb").ToList();
                                List<ResponseInfo> response = GetResponseInfo(requestList);
                                cqre = JsonConvert.DeserializeObject<QGReportDwdbList>(response[0].Data);
                            }
                            if (cqre.data != null && cqre.data.Length > 0)
                            {

                                for (int j = 0; j < cqre.data.Length; j++)
                                {
                                    QGReportDwdbInfo itemJson = cqre.data[j];
                                    Utility.ClearNullValue<QGReportDwdbInfo>(itemJson);
                                    ExternalGuarantee item = new ExternalGuarantee();
                                    item.seq_no = i;
                                    item.creditor = itemJson.more;
                                    item.debtor = itemJson.mortgagor;
                                    item.type = itemJson.priClaSecKind == "1" ? "合同" : itemJson.priClaSecKind == "2" ? "其他" : "";
                                    item.amount = itemJson.priClaSecAm;
                                    item.period = ConvertStringToDate(itemJson.pefPerForm) + "-" + ConvertStringToDate(itemJson.pefPerTo);
                                    item.guarantee_time = itemJson.guaranperiod;
                                    item.guarantee_type = itemJson.gaType;
                                    //item.guarant_scope = GetIsPublishValue(itemJson.ispublish, itemJson.rage);
                                    guarantee_items.Add(item);
                                }
                            }
                        }
                        report.external_guarantees = guarantee_items;
                    }
                }
                //股权变更
                else if (responseinfo.Name == "stockchange")
                {
                    var content = responseinfo.Data;
                    QGReportStockChangeList cqre = JsonConvert.DeserializeObject<QGReportStockChangeList>(content);
                    if (cqre.totalPage != null && cqre.totalPage > 0)
                    {
                        report.if_equity = "是";
                        List<StockChangeItem> stockChanges = new List<StockChangeItem>();
                        for (int i = 1; i <= cqre.totalPage; i++)
                        {
                            if (i > 1)
                            {
                                _request.AddOrUpdateRequestParameter("pageno", i.ToString());
                                _request.AddOrUpdateRequestParameter("start", ((i - 1) * 5).ToString());
                                List<XElement> requestList = _requestXml.GetRequestListByGroup("reportstockchange").ToList();
                                List<ResponseInfo> response = GetResponseInfo(requestList);
                                cqre = JsonConvert.DeserializeObject<QGReportStockChangeList>(response[0].Data);
                            }
                            if (cqre.data != null && cqre.data.Length > 0)
                            {

                                for (int j = 0; j < cqre.data.Length; j++)
                                {
                                    QGReportStockChangeInfo itemJson = cqre.data[j];
                                    Utility.ClearNullValue<QGReportStockChangeInfo>(itemJson);
                                    StockChangeItem item = new StockChangeItem();
                                    item.seq_no = stockChanges.Count + 1;
                                    item.name = itemJson.inv;
                                    item.before_percent = string.IsNullOrEmpty(itemJson.transAmPr) ? string.Empty : itemJson.transAmPr;
                                    if (item.before_percent != null && item.before_percent.Length > 0 && !item.before_percent.Contains(".") && !item.before_percent.Contains("%"))
                                        item.before_percent = item.before_percent + "%";
                                    item.after_percent = string.IsNullOrEmpty(itemJson.transAmAft) ? string.Empty : itemJson.transAmAft;
                                    if (item.after_percent != null && item.after_percent.Length > 0 && !item.after_percent.Contains(".") && !item.after_percent.Contains("%"))
                                        item.after_percent = item.after_percent + "%";
                                    item.change_date = string.IsNullOrEmpty(itemJson.altDate) ? string.Empty : ConvertStringToDate(itemJson.altDate);
                                    stockChanges.Add(item);
                                }
                            }
                        }
                        report.stock_changes = stockChanges;
                    }
                }
                //修改记录
                else if (responseinfo.Name == "alter")
                {
                    var content = responseinfo.Data;
                    QGReportAlterList cqre = JsonConvert.DeserializeObject<QGReportAlterList>(content);
                     if (cqre.totalPage != null && cqre.totalPage > 0)
                    {
                       List<UpdateRecord> records = new List<UpdateRecord>();
                       for (int i = 1; i <= cqre.totalPage; i++)
                       {
                           if (i > 1)
                           {
                               _request.AddOrUpdateRequestParameter("pageno", i.ToString());
                               _request.AddOrUpdateRequestParameter("start", ((i - 1) * 5).ToString());
                               List<XElement> requestList = _requestXml.GetRequestListByGroup("reportalter").ToList();
                               List<ResponseInfo> response = GetResponseInfo(requestList);
                               cqre = JsonConvert.DeserializeObject<QGReportAlterList>(response[0].Data);
                           }
                           if (cqre.data != null && cqre.data.Length > 0)
                           {

                               for (int j = 0; j < cqre.data.Length; j++)
                               {
                                   QGReportAlterInfo itemJson = cqre.data[j];
                                   Utility.ClearNullValue<QGReportAlterInfo>(itemJson);
                                   UpdateRecord item = new UpdateRecord();
                                   item.seq_no = records.Count + 1;
                                   item.update_item = itemJson.alitem;
                                   item.before_update = itemJson.altBe;
                                   item.after_update = itemJson.altAf;
                                   item.update_date = string.IsNullOrEmpty(itemJson.altDate) ? string.Empty : ConvertStringToDate(itemJson.altDate);
                                   records.Add(item);
                               }
                           }
                       }
                        report.update_records = records;
                    }
                }
                // 对外投资
                else if (responseinfo.Name == "externalinvest")
                {
                   
                    var content = responseinfo.Data;
                    QGReportInvestList cqre = JsonConvert.DeserializeObject<QGReportInvestList>(content);
                    if (cqre.data != null && cqre.data.Length > 0)
                    {
                        report.if_invest = "是";
                        List<InvestItem> investList = new List<InvestItem>();
                        for (int i = 0; i < cqre.data.Length; i++)
                        {
                            QGReportInvestInfo itemJson = cqre.data[i];
                            Utility.ClearNullValue<QGReportInvestInfo>(itemJson);
                            InvestItem item = new InvestItem();
                            item.seq_no = investList.Count + 1;
                            item.invest_name = itemJson.entName;
                            item.invest_reg_no = itemJson.uniscId;
                            investList.Add(item);
                        }
                        report.invest_items = investList;
                    }
                }              

            }
        }
        #endregion

        #region 抽查检查List
        private void LoadAndParseCheckUpList(string response)
        {
            List<CheckupInfo> list = new List<CheckupInfo>();
            LoadAndParseCheckUpItems(response, list);
            QGCcjcList cqInfo = JsonConvert.DeserializeObject<QGCcjcList>(response);
            if (cqInfo.totalPage > 1)
            {
                for (int i = 2; i <= cqInfo.totalPage; i++)
                {
                    _request.AddOrUpdateRequestParameter("pageno", i.ToString());
                    _request.AddOrUpdateRequestParameter("start", ((i - 1) * 5).ToString());
                    List<XElement> requestList = _requestXml.GetRequestListByGroup("check").ToList();
                    List<ResponseInfo> responseList = GetResponseInfo(requestList);
                    LoadAndParseCheckUpItems(responseList[0].Data, list);
                }
            }
            _checkups = list;

        }
        #endregion

        #region 抽查检查
        private void LoadAndParseCheckUpItems(String response, List<CheckupInfo> list)
        {           
            
            QGCcjcList cqInfo = JsonConvert.DeserializeObject<QGCcjcList>(response);
            if (cqInfo.data != null && cqInfo.data.Length > 0)
            {
                for (int i = 0; i < cqInfo.data.Length; i++)
                {
                    QGCcjcInfo item = cqInfo.data[i];
                    Utility.ClearNullValue<QGCcjcInfo>(item);
                    CheckupInfo checkup = new CheckupInfo();
                    checkup.name = _enterpriseInfo.name;
                    checkup.reg_no = _enterpriseInfo.reg_no;
                    checkup.province = _enterpriseInfo.province;
                    checkup.department = item.insAuth_CN;
                    checkup.type = item.insType=="1"?"抽查":"检查";
                    checkup.date = string.IsNullOrEmpty(item.insDate)? "" : ConvertStringToDate(item.insDate) ;
                    checkup.result = item.insRes_CN;
                    list.Add(checkup);
                }
            }
            
        }
        #endregion
        #region 股权变更List
        private void LoadAndParseStockChangesList(string response)
        {
            List<StockChangeItem> lst = new List<StockChangeItem>();
            LoadAndParseStockChanges(response, lst);
            QGGqbgList cqInfo = JsonConvert.DeserializeObject<QGGqbgList>(response);
            if (cqInfo.totalPage > 1)
            {
                for (int i = 2; i <= cqInfo.totalPage; i++)
                {
                    _request.AddOrUpdateRequestParameter("pageno", i.ToString());
                    _request.AddOrUpdateRequestParameter("start", ((i - 1) * 5).ToString());
                    List<XElement> requestList = _requestXml.GetRequestListByGroup("gqbg").ToList();
                    List<ResponseInfo> responseList = GetResponseInfo(requestList);
                    LoadAndParseStockChanges(responseList[0].Data, lst);
                }
            }
            _enterpriseInfo.stock_changes = lst;

        }

        #endregion

        #region 股权变更
        private void LoadAndParseStockChanges(String response, List<StockChangeItem> lst)
        {
            //股权变更

            QGGqbgList cqInfo = JsonConvert.DeserializeObject<QGGqbgList>(response);
            
            if (cqInfo != null && cqInfo.data.Length > 0)
            {
                for (int i = 0; i < cqInfo.data.Length; i++)
                {
                    QGGqbgInfo cq = cqInfo.data[i];
                    Utility.ClearNullValue<QGGqbgInfo>(cq);
                    StockChangeItem item = new StockChangeItem();
                    item.seq_no = lst.Count + 1;
                    item.name = cq.inv;
                    item.before_percent = string.IsNullOrWhiteSpace(cq.transAmPrBf) ? "" : cq.transAmPrBf + "%";
                    item.after_percent = string.IsNullOrWhiteSpace(cq.transAmPrAf) ? "" : cq.transAmPrAf + "%";
                    item.change_date = string.IsNullOrEmpty(cq.altDate) ? "" : ConvertStringToDate(cq.altDate);
                    item.public_date = string.IsNullOrEmpty(cq.publicDate) ? "" : ConvertStringToDate(cq.publicDate);
                    lst.Add(item);
                }
            }
           
        }
        #endregion

        #region 解析经营异常lIST
        private void LoadAndParseAbnormalList(string response)
        {
            List<LicenseInfo> list = new List<LicenseInfo>();
            LoadAndParseAbnormalItems(response);
           QGJyycList cqInfo = JsonConvert.DeserializeObject<QGJyycList>(response);
            if (cqInfo.totalPage > 1)
            {
                
                for (int i = 2; i <= cqInfo.totalPage; i++)
                {
                    _request.AddOrUpdateRequestParameter("pageno", i.ToString());
                    _request.AddOrUpdateRequestParameter("start", ((i - 1) * 5).ToString());
                    List<XElement> requestList = _requestXml.GetRequestListByGroup("abnormal").ToList();
                    List<ResponseInfo> responseList = GetResponseInfo(requestList);
                    LoadAndParseAbnormalItems(responseList[0].Data);
                }
            }           

        }
        #endregion

        #region 解析经营异常
        private void LoadAndParseAbnormalItems(String response)
        {
            QGJyycList cqInfo = JsonConvert.DeserializeObject<QGJyycList>(response);
            List<AbnormalInfo> list = new List<AbnormalInfo>();
            if (cqInfo.data != null && cqInfo.data.Length > 0)
            {
                for (int i = 0; i < cqInfo.data.Length; i++)
                {
                    QGJyycInfo item = cqInfo.data[i];
                    Utility.ClearNullValue<QGJyycInfo>(item);
                    AbnormalInfo dItem = new AbnormalInfo();
                    dItem.name = _enterpriseInfo.name;
                    dItem.reg_no = _enterpriseInfo.reg_no;
                    dItem.province = _enterpriseInfo.province;
                    dItem.in_reason = item.speCause_CN;
                    dItem.in_date = string.IsNullOrEmpty(item.abntime) ? "" : ConvertStringToDate(item.abntime);
                    dItem.out_reason = item.remExcpRes_CN;
                    dItem.out_date = string.IsNullOrEmpty(item.remDate) ? "" : ConvertStringToDate(item.remDate);
                    dItem.department = item.decOrg_CN;
                    list.Add(dItem);
                }
            }
            _abnormals = list;
        }
        #endregion

        #region 行政许可List
        private void LoadAndParseLicenseList(string response,int flag)
        {
            List<LicenseInfo> list = new List<LicenseInfo>();
            LoadAndParseLicenseInfo(response);
            QGXzxkList cqInfo = JsonConvert.DeserializeObject<QGXzxkList>(response);
            if (cqInfo.totalPage > 1)
            {
                string s = string.Empty;
                if(flag==1)
                {
                    s = "xzxk1";
                }
                else if (flag == 2)
                {
                    s = "xzxk2";
                }
                for (int i = 2; i <= cqInfo.totalPage; i++)
                {
                    _request.AddOrUpdateRequestParameter("pageno", i.ToString());
                    _request.AddOrUpdateRequestParameter("start", ((i - 1) * 5).ToString());
                    List<XElement> requestList = _requestXml.GetRequestListByGroup(s).ToList();
                    List<ResponseInfo> responseList = GetResponseInfo(requestList);
                    LoadAndParseLicenseInfo(responseList[0].Data);
                }
            }
           

        }
        #endregion

        #region 解析行政许可信息
        /// <summary>
        /// 解析行政许可信息
        /// </summary>
        /// <param name="requestInfo"></param>
        private void LoadAndParseLicenseInfo(string response)
        {
            //response = response.Replace("[{\"total", "{\"total").Replace("]}]", "]}");
            QGXzxkList xzxk = JsonConvert.DeserializeObject<QGXzxkList>(response);
           
            if (xzxk.data != null && xzxk.data.Length > 0)
            {
                for (int i = 0; i < xzxk.data.Length; i++)
                {
                    QGXzxkInfo item = xzxk.data[i];
                    Utility.ClearNullValue<QGXzxkInfo>(item);
                    LicenseInfo licenseinfo = new LicenseInfo();
                    licenseinfo.seq_no = _enterpriseInfo.licenses.Count + 1;
                    licenseinfo.number = item.licNo;
                    licenseinfo.name = item.licName_CN;
                    licenseinfo.start_date = string.IsNullOrEmpty(item.valFrom) ? "" : ConvertStringToDate(item.valFrom);
                    licenseinfo.end_date = string.IsNullOrEmpty(item.valTo) ? "" : ConvertStringToDate(item.valTo);
                    licenseinfo.department = item.licAnth;
                    licenseinfo.content = item.licItem;
                    licenseinfo.status = string.IsNullOrEmpty(item.status_CN) ? item.status == "1" ? "有效" : "无效" : item.status_CN;
                    _enterpriseInfo.licenses.Add(licenseinfo);
                }
            }
           
        }
        #endregion

        #region 动产抵押List
        private void LoadAndParseMortgageList(string response)
        {
            List<MortgageInfo> list = new List<MortgageInfo>();
            LoadAndParseMortgageInfoItems(response, list);
            QGDcdyList cqInfo = JsonConvert.DeserializeObject<QGDcdyList>(response);
            if (cqInfo.totalPage > 1)
            {
                for (int i = 2; i <= cqInfo.totalPage; i++)
                {
                    _request.AddOrUpdateRequestParameter("pageno", i.ToString());
                    _request.AddOrUpdateRequestParameter("start", ((i - 1) * 5).ToString());
                    List<XElement> requestList = _requestXml.GetRequestListByGroup("motag").ToList();
                    List<ResponseInfo> responseList = GetResponseInfo(requestList);
                   LoadAndParseMortgageInfoItems(responseList[0].Data,list);
                }
            }
            _enterpriseInfo.mortgages = list;

        }
        #endregion

        #region 解析动产抵押详细信息
        private void LoadAndParseMortgageDetailInfo(List<ResponseInfo> responselist, MortgageInfo mortgageinfo)
        {
            foreach (ResponseInfo responseinfo in responselist)
            {
                var content = responseinfo.Data;
                if (responseinfo.Name == "dyqr")
                {
                    List<Mortgagee> mortgagees = new List<Mortgagee>();                    
                    QGDyqrList cqInfo = JsonConvert.DeserializeObject<QGDyqrList>(content);
                    if (cqInfo.data != null && cqInfo.data.Length > 0)
                    {
                        for (int i = 0; i < cqInfo.data.Length; i++)
                        {
                            QGDyqrInfo item = cqInfo.data[i];
                            Utility.ClearNullValue<QGDyqrInfo>(item);
                            Mortgagee mortgagee = new Mortgagee();
                            mortgagee.seq_no = mortgagees.Count + 1;
                            mortgagee.name = item.more;
                            mortgagee.identify_type = item.bLicType_CN;
                            mortgagee.identify_no = item.bLicNo;
                            mortgagees.Add(mortgagee);
                        }
                        mortgageinfo.mortgagees = mortgagees;
                    }
                }
                else if (responseinfo.Name == "dyw")
                {
                    List<Guarantee> guarantees = new List<Guarantee>();// 抵押物概况

                    QGDywList cqInfo = JsonConvert.DeserializeObject<QGDywList>(content);
                    if (cqInfo.data != null && cqInfo.data.Length > 0)
                    {
                        for (int j = 0; j < cqInfo.data.Length; j++)
                        {
                            QGDywInfo item = cqInfo.data[j];
                            Utility.ClearNullValue<QGDywInfo>(item);
                            Guarantee guarantee = new Guarantee();
                            guarantee.seq_no = guarantees.Count + 1;
                            guarantee.name = item.guaName;
                            guarantee.belong_to = item.own;
                            guarantee.desc = item.guaDes;
                            guarantee.remarks = item.remark;
                            guarantees.Add(guarantee);
                        }
                    }
                    mortgageinfo.guarantees = guarantees;
                }
                else if (responseinfo.Name == "bdbzzq")
                {
                    // 被担保债权人                    
                    QGBdbzzqList cqInfo = JsonConvert.DeserializeObject<QGBdbzzqList>(content);
                    if (cqInfo.data != null && cqInfo.data.Length>0)
                    {
                        QGBdbzzqInfo item = cqInfo.data[0];
                        Utility.ClearNullValue<QGBdbzzqInfo>(item);
                        mortgageinfo.debit_type = item.priClaSecKind_CN;
                        mortgageinfo.debit_amount = item.priClaSecAm + "万" + item.regCapCur_CN;	
;
                        mortgageinfo.debit_scope = item.warCov;
                        var start_date = string.IsNullOrEmpty(item.pefPerForm) ? "" : ConvertStringToDate(item.pefPerForm);
                        var end_date = string.IsNullOrEmpty(item.pefPerTo) ? "" : ConvertStringToDate(item.pefPerTo);
                        mortgageinfo.debit_period = string.Format("自 {0} 至{1}", start_date, end_date);
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
        private void LoadAndParseMortgageInfoItems(string response,List<MortgageInfo> list)
        {            
            QGDcdyList cqInfo = JsonConvert.DeserializeObject<QGDcdyList>(response);
            if (cqInfo.data != null && cqInfo.data.Length > 0)
            {
                for (int i = 0; i < cqInfo.data.Length; i++)
                {
                    QGDcdyInfo item = cqInfo.data[i];
                    Utility.ClearNullValue<QGDcdyInfo>(item);
                    MortgageInfo mortgageinfo = new MortgageInfo();
                    mortgageinfo.seq_no = list.Count + 1;
                    mortgageinfo.number = item.morRegCNo;
                    mortgageinfo.date = string.IsNullOrEmpty(item.regiDate) ? "" : ConvertStringToDate(item.regiDate);
                    mortgageinfo.amount =string.IsNullOrEmpty(item.priClaSecAm)? item.priClaSecAm : item.priClaSecAm +"万"+ item.regCapCur_Cn;
                    mortgageinfo.status = item.type == "1" ? "有效" : item.type == "2"?"无效":"";
                    mortgageinfo.department = item.regOrg_CN;
                    mortgageinfo.public_date = string.IsNullOrEmpty(item.publicDate) ? "" : ConvertStringToDate(item.publicDate);
                    _request.AddOrUpdateRequestParameter("dcdydetailId", item.morReg_Id);
                    List<XElement> requestList = null;
                    List<ResponseInfo> responseList = null;
                    requestList = _requestXml.GetRequestListByGroup("motage").ToList();
                    responseList = GetResponseInfo(requestList);
                    LoadAndParseMortgageDetailInfo(responseList, mortgageinfo);
                    list.Add(mortgageinfo);
                }
            }
          
        }
        #endregion

        #region 股权出质List
        private void LoadAndParseEquityQualityList(string response)
        {
            List<EquityQuality> list = new List<EquityQuality>();
            LoadAndParseEquityQualityItems(response, list);
            QGGqczList cqInfo = JsonConvert.DeserializeObject<QGGqczList>(response);
            if (cqInfo.totalPage > 1)
            {
                for (int i = 2; i <= cqInfo.totalPage; i++)
                {
                    _request.AddOrUpdateRequestParameter("pageno", i.ToString());
                    _request.AddOrUpdateRequestParameter("start", ((i - 1) * 5).ToString());
                    List<XElement> requestList = _requestXml.GetRequestListByGroup("gqcz").ToList();
                    List<ResponseInfo> responseList = GetResponseInfo(requestList);
                    LoadAndParseEquityQualityItems(responseList[0].Data, list);
                }
            }
            _enterpriseInfo.equity_qualities = list;

        }
        #endregion

        #region 解析股权出质登记信息
        /// <summary>
        /// 解析股权出质登记信息
        /// </summary>
        /// <param name="cqInfo"></param>
        private void LoadAndParseEquityQualityItems(String response, List<EquityQuality> list)
        {
            QGGqczList cqInfo = JsonConvert.DeserializeObject<QGGqczList>(response);
            
            if (cqInfo.data != null && cqInfo.data.Length > 0)
            {                
                for (int i = 0; i < cqInfo.data.Length; i++)
                {
                    QGGqczInfo item = cqInfo.data[i];
                    Utility.ClearNullValue<QGGqczInfo>(item);
                    EquityQuality equityquality = new EquityQuality();
                    equityquality.seq_no = list.Count + 1;
                    equityquality.number = item.equityNo;
                    equityquality.pledgor = item.pledgor;
                    equityquality.pledgor_identify_no = item.pledBLicNo;
                    equityquality.pledgor_amount = Convert.ToString(item.impAm);
                    equityquality.pawnee = item.impOrg;
                    equityquality.pawnee_identify_no = item.impOrgBLicNo;
                    equityquality.date = string.IsNullOrEmpty(item.equPleDate) ? "" : ConvertStringToDate(item.equPleDate);
                    equityquality.status = item.type=="1"?"有效":item.type=="2"?"无效":"";
                    equityquality.public_date = string.IsNullOrEmpty(item.publicDate) ? "" : ConvertStringToDate(item.publicDate);
                    if (item.vStakQualitInfoAlt != null && item.vStakQualitInfoAlt.Length>0)
                    {
                        int seq=1;
                        List<ChangeItem> its = new List<ChangeItem>();
                        foreach(var update in item.vStakQualitInfoAlt)
                        { 
                            QGGqczInnerInfo innerinfo = update;
                            Utility.ClearNullValue<QGGqczInnerInfo>(innerinfo);
                            ChangeItem it = new ChangeItem();
                            it.change_content = innerinfo.alt;
                            it.seq_no = seq;
                            it.change_date = ConvertStringToDate(innerinfo.altDate);
                            its.Add(it);                            
                        }
                        equityquality.change_items=its;
                    }
                    list.Add(equityquality);
                }
            }
          
        }
        #endregion

        #region 股东信息List
        private void LoadAndParsePartnersList(string response)
        {           
            List<Partner> partnerList = new List<Partner>();
            LoadAndParsePartners(response, partnerList);
            QGPartnerList cqInfo = JsonConvert.DeserializeObject<QGPartnerList>(response);
            if(cqInfo.totalPage>1)
            {
                for (int i = 2; i <= cqInfo.totalPage; i++)
                {
                    _request.AddOrUpdateRequestParameter("pageno", i.ToString());
                    _request.AddOrUpdateRequestParameter("start", ((i - 1) * 5).ToString());
                    List<XElement> requestList = _requestXml.GetRequestListByGroup("partner").ToList();
                    List<ResponseInfo> responseList = GetResponseInfo(requestList);
                    LoadAndParsePartners(responseList[0].Data, partnerList);
                }
            }
            _enterpriseInfo.partners = partnerList;
           
        }
        #endregion

        #region 投资信息
        private void LoadAndParsePartners(string response, List<Partner> partnerList)
        {
            HtmlDocument hd = new HtmlDocument();
            Regex re = new Regex("");
            if (response.Contains("/index/invalidLink"))
                return;
            QGPartnerList cqInfo = JsonConvert.DeserializeObject<QGPartnerList>(response);
            
            if (cqInfo.data != null && cqInfo.data.Length > 0)
            {
                for (int i = 0; i < cqInfo.data.Length; i++)
                {
                    QGPartnerInfo item = cqInfo.data[i];
                    Partner partner = new Partner();
                    Utility.ClearNullValue<QGPartnerInfo>(item);
                    if (item.bLicNo != null && item.bLicNo.Length > 0)
                    {
                        hd.LoadHtml(item.bLicNo);
                        var divnodes = hd.DocumentNode.SelectNodes("./div");
                        if(divnodes==null)
                        {
                            divnodes = hd.DocumentNode.SelectNodes("./span");
                        }
                        var no = hd.DocumentNode.InnerText.Trim();
                        if (divnodes != null && divnodes.Any())
                        {
                            foreach (HtmlNode div in divnodes)
                            {
                                no = no.Replace(div.InnerText.Trim(), "");
                            }
                        }
                        partner.identify_no = no.Replace(" ","");
                    }

                    partner.identify_type = string.IsNullOrEmpty(item.blicType_CN) ? "非公示项" : item.blicType_CN;
                    if (item.invType_CN != null && item.invType_CN.Length > 0)
                    {
                        hd.LoadHtml(item.invType_CN);
                        Regex reg1 = new Regex("([a-zA-Z]*\\d+[a-zA-Z]*)+");
                        var type = reg1.Replace(hd.DocumentNode.InnerText, "");
                        partner.stock_type = type.Replace("=","").Trim();
                    }
                    partner.stock_name = item.inv;
                    partner.seq_no = partnerList.Count + 1;
                    partner.stock_percent = "";
                    partner.total_should_capi = item.liSubConAm;
                    partner.total_real_capi = item.liAcConAm;
                    partner.should_capi_items = new List<ShouldCapiItem>();
                    partner.real_capi_items = new List<RealCapiItem>();
                    string url = string.Format("http://www.gsxt.gov.cn/corp-query-entprise-info-shareholderDetail-{0}.html", item.invId);
                    ////string ur = string.Format("http://cq.gsxt.gov.cn/gsxt/api/efactcontribution/queryList/{0}?currentpage=1&pagesize=5&t=1482749460142", item.invid);
                    var result = request.HttpPost(url, string.Empty, true);//_request.htt(ur, string.Empty, "CQ");
                    BsonDocument document = BsonDocument.Parse(result);
                    if (document != null)
                    {
                        BsonArray array = document["data"].AsBsonArray;
                        foreach (BsonArray inner_item in array)
                        {
                            if (inner_item == null || !inner_item.Any())
                            {
                                continue;
                            }
                            foreach (var arr_item in inner_item)
                            {
                                RealCapiItem realItem = new RealCapiItem();
                                ShouldCapiItem shouldItem = new ShouldCapiItem();
                                // var tems = arr_item.ToJson();
                                if (arr_item.ToJson().Contains("acConAm"))
                                {
                                    realItem.real_capi = arr_item["acConAm"].IsInt32?arr_item["acConAm"].AsInt32 + "万元":arr_item["acConAm"].IsDouble?arr_item["acConAm"].AsDouble + "万元":"";
                                    realItem.invest_type = arr_item["conForm_CN"].AsString; ;
                                    realItem.real_capi_date = arr_item["conDate"].IsBsonNull ? "" : ConvertStringToDate(arr_item["conDate"].AsInt64.ToString());                                    
                                    partner.real_capi_items.Add(realItem);
                                }
                                //应缴
                                else if (arr_item.ToJson().Contains("subConAm"))
                                {
                                    shouldItem.shoud_capi = arr_item["subConAm"].IsInt32 ? arr_item["subConAm"].AsInt32 + "万元" : arr_item["subConAm"].IsDouble ? arr_item["subConAm"].AsDouble + "万元" : ""; 
                                    shouldItem.invest_type = arr_item["conForm_CN"].AsString;
                                    shouldItem.should_capi_date = arr_item["conDate"].IsBsonNull?"":ConvertStringToDate(arr_item["conDate"].AsInt64.ToString());
                                    
                                    partner.should_capi_items.Add(shouldItem);
                                }


                            }
                        }
                    }
                    if (partner.should_capi_items.Count == 0 || partner.real_capi_items.Count == 0)
                    {
                        if (partner.should_capi_items.Count == 0)
                        {
                            ShouldCapiItem shouldItem = new ShouldCapiItem();
                            shouldItem.shoud_capi = string.IsNullOrEmpty(item.liSubConAm ) ? "" : item.liSubConAm + "万元";
                            if (shouldItem.shoud_capi.Length > 1 && !shouldItem.shoud_capi.Equals("0万元"))
                            {
                                partner.should_capi_items.Add(shouldItem);
                            }
                        }
                        if (partner.real_capi_items.Count == 0)
                        {
                            RealCapiItem realItem = new RealCapiItem();
                            realItem.real_capi =string.IsNullOrEmpty(item.liAcConAm) ? "" : item.liAcConAm + "万元";
                            if (realItem.real_capi.Length > 1 && !realItem.real_capi.Equals("0万元"))
                            {
                                partner.real_capi_items.Add(realItem);
                            }
                        }
                    }

                    partnerList.Add(partner);
                }
            }
            
        }
         #endregion

        #region 解析主要人员
        /// <summary>
        /// 解析主要人员
        /// </summary>
        /// <param name="response"></param>
        private void LoadAndParseStaffs(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return;
            QGEmployeeList cqInfos = JsonConvert.DeserializeObject<QGEmployeeList>(response);
            
            if (cqInfos.data != null && cqInfos.data.Length > 0)
            {
                this.LoadAndParseEmployeeContent(cqInfos);
                if (cqInfos.totalPage > 1)
                {
                    for (int i = 2; i <= cqInfos.totalPage; i++)
                    {
                        var request = this.CreateRequest();
                        var startPage = (i - 1) * 16;
                        request.AddOrUpdateRequestParameter("startPage", startPage.ToString());
                        var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("employee_page"));
                        if (responseList != null && responseList.Any())
                        {
                            cqInfos = JsonConvert.DeserializeObject<QGEmployeeList>(responseList.First().Data);
                            this.LoadAndParseEmployeeContent(cqInfos);
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析主要成员信息
        void LoadAndParseEmployeeContent(QGEmployeeList cqInfos)
        {
            HtmlDocument hd = new HtmlDocument();
            for (int i = 0; i < cqInfos.data.Length; i++)
            {
                var position = string.Empty;
                QGEmployeeInfo item = cqInfos.data[i];
                Utility.ClearNullValue<QGEmployeeInfo>(item);
                Employee employee = new Employee();
                employee.seq_no = _enterpriseInfo.employees.Count + 1;
                employee.sex = "";
                employee.cer_no = "";
                hd.LoadHtml(item.position_CN);
                var node = hd.DocumentNode.SelectSingleNode("./img");
                if (node != null)
                {
                    imgAddr.Add(imgAddr.Count.ToString(), node.Attributes["src"].Value);
                    var temp = node.Attributes["src"].Value.ToString();
                    if (node.Attributes["src"].Value.ToString().Equals("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAB0AAAAOCAYAAADT0Rc6AAABaUlEQVR42mNgwA7SgNgUiV8OxEoM\nxAN7II4lRmERENtC2cuBOBzKlgbiL0DMg0VPIhDPxoIPAvE1HHJpyAYsRLII2dIuIH4MFUPGWUCs\nB8ReWPAEIN6CQ86AkKXyQPwBiP2waNZD0rsJagkMXwLi+2hi27AFLzZLdwNxLdTyHUAsiyNqPqHx\nk4F4CprYD2IsrYS6jgkqVgDE74DYEYelhHyK1VKQywKwxCky8IHGLxeSmCU0pQYg4SlQByOLgdSI\nY7OYDYpBhmsh8ZExH5qeLKglyHgvEF9BE7sLTQsoAJQl/gPxayjGx4ZlH0do/KHjhVCLkcXOQVO1\nG7qln3BE/A+0+ONBCtpwLHgmNOEhi50E4g6ksoBsSxmgrl+Iho8C8Q00sYe4gpccS62B2AUN90Hz\nLrLYNiiNUgSuAeJfSCXOHxzsX1C1RWiO5oOm0HCoT3MIlbvGOIosfNgYzQxQfl4MdVgPjrIaDAC5\nfoW6ShoocQAAAABJRU5ErkJggg=="))
                        position = "监事";
                    else if (node.Attributes["src"].Value.Equals("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAB0AAAAOCAYAAADT0Rc6AAABoUlEQVR42r2UT0REURTGr5G0eCJJ\nWkWrFrNLxshom1m0mM1IRkak1WgRLUbSLmmRJDIyRtImaTFGJC3Sov0s0ma0GEkkSVrE9J18L2fu\nvDtv2nT5effdd+89/77zjGkefWDV/PNYA0ecz4JPBxPcMwkyCjkzB94c1E1AlE9gOMSpG/W+AHbp\nyD7YBvNck5EEp5x303DT2ALr/CgX9Frfl8E9GAhw5hl4nIvRPd4zDc4492yjMV7YA/LgUn2TzUXQ\nACuODNhGa6AMbsEj5xXbaJEpksVXpjjC9EkdCmCcB6tgKsRoR+mNEIlwEUTpodQvYRlI0REpxQ4v\navBZo9EyDfoaSDLVLTXNK6/8lHsOhogr0jtQYhAPnB/aRsWLL7bLCTjmWp28gA/1vtkmvV3Uhp1e\no/b89tsByHJjv3Vpho6YDoSUowaEc6a8oBgNukBqG+fzL0Zn2CqigTTZoILTikH/UJx9KjV4p0o7\niVR695qluaIu9LDT26LIJTCmRHShqFLNei1H0aRUDbMUTSlASD6uXv/5JSZCGAk4F2V07Yh9A2Qg\ng+AVrBiLAAAAAElFTkSuQmCC"))
                        position = "经理";
                    else if (node.Attributes["src"].Value.Equals("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAADkAAAAOCAYAAACVZ7SQAAACqElEQVR42r2WUYSUURTHx1irh8RK\nkqzoIVlj7dsaayWykmRfMg9ZSezDyDysXpL00EvWWvMwljWStTIkSdaItVZW1rL2IUkiWaOHLMk+\njQx1Tn43Z447830zDx1+Zu65937f/d97zrlfJtNui8IR0x4Qqpl0dkWYyPRnt4Qx0x7Dl9YuCDNp\nBx8IR017UGilmHdC+C4UaK90QTeuyOYFPgvrpr2Oz44pIrwa4a3wsUPfbBqRzQSBOn5HWDa+Qhey\nwqhwyVAXHpj2PeEl/y8LU8wZJWI8ZWGtQ9+/CFlFzG9+n4P1NU1fsOPCO+EV7VM9hNh9xL0RvkKD\njd5k0Ws824fua9OvvGe+9dXThOsAIg45UeU6OxzsEaJ17G1hN6XAEXeSgccIiPWNmPmH7nn67orz\nNdPmZE74YdrTTuSQOcEDCk+JF3aiRJGI9WkufujQN+NEJp1kV5G6Y+Psqi5o24msuTlZwusZ7ZjI\nFmEXRIZcrjm2KTjenzPvyyN42lAhPK1Px5z0JfgJ6rVK3mUx2t5KEFkjbytdwlOfM+l8mgobJhUG\nyb0l56sTrsGKkU3ciETAFwpPWxF4SBjYcF0yQocjIheEPfKyH5ENV+43I1fBvhF5kfzzrCDU+vao\nulNJOamhcxUBWuKvkZNZ7sY8i63+J5H5DtfSMlXa+nZIucluIlXQNwTZrxoNn/MsMFg/IvU9c/wG\nZlmw9WnanDPzypEPDL3GPjnfvg9XL3KIQSXXr+J+cpW86EFkK/LJN05UWHbJJe+3cyci18ti5Orx\nufx3l28IvwjDLUInZg1OuJQgUqvtU17WoogEO+0qYaAcqZKBYff8Y1TQAid5J+lyvskRz9GeN3eg\ntxwCB4yvRNWzNo/wMrlk7QwL7IWzkatrlUK44GpJm/0B+H4BqS3ysBwAAAAASUVORK5CYII="))
                        position = "执行董事";
                    else if (node.Attributes["src"].Value.Equals("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAB0AAAAOCAYAAADT0Rc6AAABdklEQVR42qWUMUgDQRBFDwsrsbGw\nEBEsLIJIOpEgIkgQK7tUV4iQIoiFnYi9hcgVQZBUYmEnInIIIQQLEUFSiIgIIiGlnVVKZ+EffL4T\nTnHhkezfnZ3d2b8XRX7bMIrUL0L7bVsy4rxJNaNBvBlN6jeh8ZwaNtJwuDVeBoxVs6RzxgqRGvvU\n3zUu8H/VKCMmsOaQGNcDxriC0R6S3RgfoGd8Gm0sErh0Sn1F44EnxLOWankLctKMAyzojRUo/kvW\n2zTqovU1aYxJSrjL5wFjsSTNO2nfM9OIcS7cw0Cqz1LcAjawTtRRTtbCnHFNOma0jGEi3N2xaCnK\ny87XKrScCr3DSD+S9sTebcf6XUq6jPtTTpGYtQ5cXf5v0lDaisMJXgFrDzDmot7pDn4zqliAtW1j\nhuISnIy5M15F63rlnccHgHnEXaheoriS85yOnKemXogmxGkZiePCjEnZ9CgcWsFJt/K+vVMI+AvT\nssaQcYYndYircNs3bFSWiVYudnIAAAAASUVORK5CYII="))
                        position = "董事";
                    else if (node.Attributes["src"].Value.ToString().Equals("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACsAAAAOCAYAAAC2POVFAAAB/0lEQVR42rWVQUSDYRjHJ5MOiUw6\nTCJJJtltMpmYyQ7pNh12SOwwSbol6dClQ7LDRHaaDpEkySdmpkMS0yFJIpkOHbqlw+xSz5v/x9Pj\n+fZ+O/Tys33P+z7f+7zP+3+eLxDQxyIRZc9R2PyOBJH1sS5CFC1rgtKQJ0qMZ6LCniuw8TV5HKCk\ncEU8eszl2L5J4sMjyH6iTBzLiUk4ujjEJnteJ07xf5ZIwceQVigQFx5zUR/BLhHvxAkxoJ1kA0Fe\nEq/gDS+rYXPDmSKJczZvuIc/tznKnjzYbtyWucEnHMxTO0mFHQSizUWY/6eSGanFZptg+4gvok4s\n2ISexcslRqsPHnNZEawts01LZoc6KOBAL3EkuMG1SPsE85tC4POMIq6d28yaQfiYwvnukD83FSKq\n0I6L0ea+sDnICO8kMutV5UZeFB3KAssiQdYRQkHxNlNTWlCDBTsDfUrKCJjb7tAlUm2C7cJ+uf8I\n1kggo3CArsJttyjYaUvrikHfSZtm1/DrksPG3LZCjDG/AjLJuUb74baGDxmME2EcrgWJqSOGxs+p\nQ2vSHmd+caWt7Sktz1Gy5QabwHyLfTTSmDPymeNOYVG5LgWlql1kq+lDgWSQ2WUfdbKKSjdBbSlf\nqxCKswUJ/Y5hbNQJI+LFpjgO0dp2IRnbGCW2iR7LOtPygj81EMiFCWOxNQAAAABJRU5ErkJggg=="))
                        position = "董事长";
                    else if (node.Attributes["src"].Value.ToString().Equals("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACsAAAAOCAYAAAC2POVFAAACPUlEQVR42sWVX2SVYRzHHzNHcsSR\nJF2MyWR2l5kk3SXnYjKOYzKZMV0dk9jFJF3EZCZJzMwcSczMLmZipovM7H4XGZkuJhlJki5ifX98\n3vl59j7vToz9+DjP8573eZ7fn+/ze0M4alfEg5znDf7Ls4p4HE7BLog9Meie3RTfxMXEmifiLeN7\n4k+CG7xzSww5bM198TPBXt6hVZgUz9z8s5h28zirFkhHQQIsmA03HxWvCGBGvBAjPMv8WGJcwuEj\ndiAWxLsEC7zjbUo8ZVM7+Fz0/yOxQ8Vi2xdlxubsa/bpF8uMy0XOlgoyVIqc7cORM2JCrLv/7JB5\n3h9P7Bc7uytWxJb4ynj1pJydp5S22Q+k0EaZTWezopcDt8WdY5z9bxnUxN0EtcjZNling/SQkQ0u\npbcBAjDJvMSBA353cXYFRzONV5HEiWl2wmUhk0Y5wSVIZfaTaBL8F8ZvUs5WyVSW/k3RHWWy5uYW\n9V/a1iIB9ZNB47v47ebPC2TQjvZjGQT3zqHNobUq5Qj0wB3aU6DP2qGdrl/aumHWnY/2HCKA0MIF\na6Bx4z3SmHVczRadJQMdkbMBjdnz27ShUfpuJTrYsn7dVaZVZwdpWabxOkzSEeqOww/SQ6LJSrCW\ns/E4mwbKPo1zU2jsF7e+lcxa0B+R0Ad0H8txKRVhnYPNLpPlpivBHF+pXqehdm74mLjmLteaY5vu\n4J81uEwDTqPDnNfMuWAZqV4duqIS1GlLx1mFchbRmbOux33KU/T9A1cGuQIA/VG9AAAAAElFTkSu\nQmCC"))
                        position = "总经理";
                    else if (node.Attributes["src"].Value.ToString().Equals("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAADkAAAAOCAYAAACVZ7SQAAACk0lEQVR42rWWQWRcURSGn4roYoSI\n6qKqVESMitlVRUSJqCxidhExiwgjYlR1U1XVRZUsqkY9pSIisggREREjjIgsIsLoIqqqVEXULGYX\nXTxPaM+p/9bpce6dN6WXz9x37rtzz3/vOee+KOq8dREznrFZoiCeC7BlbaNEKcN7eSLO4OdfbYW4\nCPBWvJsjEvQXiCXBF6IunuuwyXcWIHzJ4JD45BkrCx/GiJZHXC+xSmz8wwFGW0RRiRzCgo4a8Vw8\nP8U87j8gxjGHmTCoEruesUIGkXNEk9gkrlkiFvHCd0ETdp9Ibs8gbo/4Bs7hxAGcZraN0N0R48wp\n5ktbzfBViuxGdHDEfMaGeFuMndA7EwdE5tVJOhYhwBrLi/+/CKznWhIQ2UP8IBrEdJZwjJETOkdC\nIksY13AufvSMlZTIdieZtDnJm53kHC9eUXlQEU75wpWf1xXHCB9tvyPm3cN/FwUxwlPa+J3rmMMF\n5WeH/ImMq/gDH7mAyD5iH7nh4Nx7p2w1nEAkKrM+5X0jAr4aeaYLTwkbG2w5hI7Lm1T0GxASEnmu\nwvzAuArOhMj7yD/NKoRK2wdU3fGAyCtYr5xF5AhIRf/kP4jkUJ0yeI8qLW0nKGQjba6Qu/BrLCQy\nEblzKfpNCBnC3WPl5GP8OspwWNoeEgNiXtVIiyNcA9J2liFcB4kb2JQUqWCKbHnKdh0i5RdFonZw\nS9FALmn7sJg3bFwvb4yrp2acjhM5ivFUfCxMYIzDfNI6SVe2L0W/BZFPUAj2ICDC7hUNqkaVdOiS\n34PCMYWTrGS4CR6hcrJvL4yvmz74moqPmd/VdV1VQ8crbMI8sYz8crt2Cw52wm3lEBeNNaz/Gmu1\na/3ES/gdanz1dP0CG38SpdFXf2kAAAAASUVORK5CYII="))
                        position = "副董事长";
                    else
                        position = string.Empty;
                }
                employee.job_title = (string.IsNullOrWhiteSpace(position) && item.position_CN.Length < 20) ? item.position_CN : position;

                hd.LoadHtml(item.name);
                var rootNode = hd.DocumentNode;
                StringBuilder sb = new StringBuilder();
                rootNode.ChildNodes.Where(p => p.Name == "#text").ToList().ForEach(p=>sb.Append(p.InnerText));
                employee.name = sb.ToString();

                _enterpriseInfo.employees.Add(employee);
            }

        }
        #endregion

        #region 变更信息List
        private void LoadAndParseChangeRecordsList(string response)
        {
            List<ChangeRecord> changeRecordList = new List<ChangeRecord>();
            LoadAndParseChangeRecords(response, changeRecordList);
            QGalterList cqInfo = JsonConvert.DeserializeObject<QGalterList>(response);
            if (cqInfo.totalPage > 1)
            {
                for (int i = 2; i <= cqInfo.totalPage; i++)
                {
                    _request.AddOrUpdateRequestParameter("pageno", i.ToString());
                    _request.AddOrUpdateRequestParameter("start", ((i - 1) * 5).ToString());
                    List<XElement> requestList = _requestXml.GetRequestListByGroup("alter").ToList();
                    List<ResponseInfo> responseList = GetResponseInfo(requestList);
                    LoadAndParseChangeRecords(responseList[0].Data, changeRecordList);
                }
            }
           _enterpriseInfo.changerecords = changeRecordList;

        }
        #endregion

        #region 解析变更信息
        private void LoadAndParseChangeRecords(string response, List<ChangeRecord> changeRecordList)
        {
           
            QGalterList cqInfo = JsonConvert.DeserializeObject<QGalterList>(response);
            
            if (cqInfo.data != null && cqInfo.data.Length > 0)
            {
                for (int i = 0; i < cqInfo.data.Length; i++)
                {
                    QGalterInfo item = cqInfo.data[i];
                    Utility.ClearNullValue<QGalterInfo>(item);
                    ChangeRecord changeRecord = new ChangeRecord();
                    HtmlDocument hd = new HtmlDocument();
                    hd.LoadHtml(item.altItem_CN);
                    StringBuilder sb = new StringBuilder();
                    hd.DocumentNode.ChildNodes.Where(p => p.Name == "#text").ToList().ForEach(p => sb.Append(p.InnerText));
                    changeRecord.change_item = sb.ToString();
                    changeRecord.before_content = item.altBe;
                    changeRecord.after_content = item.altAf;
                    changeRecord.change_date =ConvertStringToDate( item.altDate);
                    changeRecord.seq_no = changeRecordList.Count() + 1;
                    changeRecordList.Add(changeRecord);
                }
            }
           
        }
        #endregion

        #region 解析分支机构
        private void LoadAndParseBranches(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return;
            QGBranchList cqInfos = JsonConvert.DeserializeObject<QGBranchList>(response);
            if (cqInfos != null && cqInfos.data.Length > 1)
            {
                this.LoadAndParseBranchContent(cqInfos);
                if (cqInfos.totalPage > 1)
                {

                }
                for (int index = 2; index <= cqInfos.totalPage; index++)
                {
                    var request = this.CreateRequest();
                    var startPage = (index - 1) * 9;
                    request.AddOrUpdateRequestParameter("startPage", startPage.ToString());
                    var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("branch_page"));
                    if (responseList != null && responseList.Any())
                    {
                        cqInfos = JsonConvert.DeserializeObject<QGBranchList>(responseList.First().Data);
                        this.LoadAndParseBranchContent(cqInfos);
                    }

                }
            }

        }
        #endregion

        #region 解析分支机构信息
        void LoadAndParseBranchContent(QGBranchList cqInfos)
        {
            if (cqInfos.data != null && cqInfos.data.Length > 0)
            {
                for (int i = 0; i < cqInfos.data.Length; i++)
                {
                    QGBranchInfo item = cqInfos.data[i];
                    Utility.ClearNullValue<QGBranchInfo>(item);
                    Branch branch = new Branch();
                    branch.belong_org = item.regOrg_CN;
                    branch.name = item.brName;
                    branch.seq_no = _enterpriseInfo.branches.Count + 1;
                    branch.oper_name = "";
                    branch.reg_no = item.regNo;
                    _enterpriseInfo.branches.Add(branch);
                }
            }
        }
        #endregion

        #region 基本信息
        public void LoadAndPaseBasicInfo(string html)
        {
            List<string> result = new List<string>();
            HtmlDocument hd = new HtmlDocument();
            hd.LoadHtml(html);
            var entType = hd.DocumentNode.SelectSingleNode("//input[@id='entType']");
            
            if (entType != null)
            {
                _entType = entType.Attributes["value"].Value;
            }
            HtmlNode nodes1 = hd.DocumentNode.SelectSingleNode("//div[@class='mainContent']");
            HtmlNodeCollection nodes2 = hd.DocumentNode.SelectNodes("//div[@class='details clearfix']/div/dl");

            #region 营业执照信息
            if (nodes2 != null && nodes2.Count > 0)
            {
                foreach (HtmlNode node in nodes2)
                {
                    switch (node.SelectSingleNode("./dt").InnerText.Trim().Replace(":", "：").Replace("：", ""))
                    {
                        case "统一社会信用代码":
                            Regex regs = new Regex("[\u4e00-\u9fa5]+");
                            var number = regs.Replace(node.SelectSingleNode("./dd").InnerText.Trim(), "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace("<!--  -->", "").Replace(" ", "");
                            _enterpriseInfo.credit_no = number;
                            break;
                        case "注册号":
                            Regex regs2 = new Regex("[\u4e00-\u9fa5]+");
                            _enterpriseInfo.reg_no = regs2.Replace(node.SelectSingleNode("./dd").InnerText.Trim(), "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace("<!--  -->", "").Replace(" ", "");
                            break;
                        case "企业名称":
                        case "名称":
                            _enterpriseInfo.name = node.SelectSingleNode("./dd").InnerText.Trim();
                            break;
                        case "类型":
                            _enterpriseInfo.econ_kind = node.SelectSingleNode("./dd").InnerText.Trim();
                            break;
                        case "注册资本":
                            _enterpriseInfo.regist_capi = node.SelectSingleNode("./dd").InnerText.Trim();
                            break;
                        case "法定代表人":
                        case "负责人":
                        case "股东":
                        case "经营者":
                        case "执行事务合伙人":
                        case "投资人":
                            _enterpriseInfo.oper_name = node.SelectSingleNode("./dd").InnerText.Trim();
                            break;
                        case "成立日期":
                        case "登记日期":
                        case "注册日期":
                            _enterpriseInfo.start_date = node.SelectSingleNode("./dd").InnerText.Trim();
                            break;
                        case "营业期限自":
                            _enterpriseInfo.term_start = node.SelectSingleNode("./dd").InnerText.Trim();
                            break;
                        case "营业期限至":
                            _enterpriseInfo.term_end = node.SelectSingleNode("./dd").InnerText.Trim();
                            break;
                        case "登记机关":
                            _enterpriseInfo.belong_org = node.SelectSingleNode("./dd").InnerText.Trim();
                            break;
                        case "核准日期":
                            _enterpriseInfo.check_date = node.SelectSingleNode("./dd").InnerText.Trim();
                            break;
                        case "登记状态":
                            _enterpriseInfo.status = node.SelectSingleNode("./dd").InnerText.Trim();
                            break;
                        case "组成形式":
                            _enterpriseInfo.type_desc = node.SelectSingleNode("./dd").InnerText.Trim();
                            break;
                        case "住所":
                        case "经营场所":
                        case "营业场所":
                        case "主要经营场所":
                            Address address = new Address();
                            address.name = "注册地址";
                            address.address = node.SelectSingleNode("./dd").InnerText.Trim();
                            address.postcode = "";
                            _enterpriseInfo.addresses.Add(address);
                            break;
                        case "经营范围":
                        case "业务范围":
                            _enterpriseInfo.scope = node.SelectSingleNode("./dd").InnerText.Trim();
                            break;
                    }
                }
            }
            #endregion

            if (nodes1 != null)
            {
                Regex reg = new Regex("(?<=var)(.+?)(?=\r\n)");
                var mat = reg.Matches(nodes1.InnerText.Replace("\r\n    <!-- 司法协助", ";\r\n    <!-- 司法协助"));  //(提取匹配到的内容)                
                foreach (Match item in mat)
                {
                    var temp = item.Groups[1].Value;
                    var value = temp.Replace("\"", "").Replace("\"", "").Replace(";", "").Replace("\r", "").Replace("\n", "").Replace("；", "").Replace(" ", "").Split('=');
                    switch (value[0].Trim())
                    {
                        case "anCheYearInfo":
                            _request.AddOrUpdateRequestParameter("reportListId", value[1]);
                            break;
                        case "alterInfoUrl":
                            _request.AddOrUpdateRequestParameter("alterInfoId", value[1]);
                            break;
                        case "gtAlertInfoUrl":
                            _request.AddOrUpdateRequestParameter("gtAlertInfoId", value[1]);
                            break;
                        case "keyPersonUrl":
                            _request.AddOrUpdateRequestParameter("personInfoId", value[1]);
                            break;
                        case "gtKeyPersonUrl":
                            _request.AddOrUpdateRequestParameter("gtPersonInfoId", value[1]);
                            break;
                        case "branchUrl":
                            branchesId = value[1];
                            _request.AddOrUpdateRequestParameter("branchInfoId", value[1]);
                            break;
                        case "shareholderUrl":
                            _request.AddOrUpdateRequestParameter("partnerInfoId", value[1]);
                            break;
                        case "mortRegInfoUrl":
                            _request.AddOrUpdateRequestParameter("mortRegInfoId", value[1]);
                            break;
                        case "mortRegDetailInfoUrl":
                            _request.AddOrUpdateRequestParameter("mortRegDetailInfoId", value[1]);
                            break;
                        case "stakQualitInfoUrl": 
                            _request.AddOrUpdateRequestParameter("stakQualitInfoId", value[1]);  //股权出质登记信息
                            break;
                        case "proPledgeRegInfoUrl":
                            _request.AddOrUpdateRequestParameter("proPledgeRegInfoId", value[1]); //基本信息知识产权出质登记信息
                            break;
                        case "insproPledgeRegInfoUrl":
                            _request.AddOrUpdateRequestParameter("insproPledgeRegInfoId", value[1]); //即时信息知识产权出质登记信息
                            break;
                        case "spotCheckInfoUrl":
                            _request.AddOrUpdateRequestParameter("spotCheckInfoId", value[1]); //抽查检查信息
                            break;
                        case "assistUrl":
                            _request.AddOrUpdateRequestParameter("assistInfoId", value[1]); //司法协助信息
                            break;
                        case "insInvAlterStockinfoUrl":
                            _request.AddOrUpdateRequestParameter("insInvAlterStockInfoId", value[1]); //股东及出资信息中的股权变更信息
                            break;
                        case "insAlterstockinfoUrl":
                            _request.AddOrUpdateRequestParameter("insAlterstockInfoId", value[1]); //股权变更信息
                            break;
                        case "insInvinfoUrl":
                            _request.AddOrUpdateRequestParameter("insInvInfoId", value[1]); //股东及出资信息，finaina字段
                            break;
                        case "insLicenceinfoUrl":
                            _request.AddOrUpdateRequestParameter("insLicenceInvInfoId", value[1]); //即时行政许可信息
                            break;
                        case "insProPledgeRegInfoUrl":
                            _request.AddOrUpdateRequestParameter("insProPledgeRegInfoId", value[1]); //即时信息中知识产权出质登记信息
                            break;
                        case "insPunishmentinfoUrl":
                            _request.AddOrUpdateRequestParameter("insPunishmentInfoId", value[1]); //即时信息中行政处罚信息
                            break;
                        case "otherLicenceDetailInfoUrl":
                            _request.AddOrUpdateRequestParameter("otherLicenceDetailInfoId", value[1]); //其他信息行政许可
                            break;
                        case "entBusExcepUrl":
                            _request.AddOrUpdateRequestParameter("entBusExcepInfoId", value[1]); //经营异常
                            break;
                        case "punishmentDetailInfoUrl":
                            _request.AddOrUpdateRequestParameter("punishmentDetailInfoId", value[1]); //行政处罚
                            break;
                        case "indBusExcepUrl":
                            _request.AddOrUpdateRequestParameter("gtAbnormalInfoId", value[1]); 
                            break;
                        case "argBusExcepUrl":
                             _request.AddOrUpdateRequestParameter("hzsAbnormalInfoId", value[1]); 
                            break;
                    }
                    
                }

                //string reportHerf = aNode.Attributes["href"].Value;
                //string uuid = Regex.Split(reportHerf, "uuid=")[1];
                //var request = CreateRequest();
                //request.AddOrUpdateRequestParameter("punish_uuid", uuid);
                //List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("punishmentDetail"));
               
            }
           
        }
        #endregion

        #region ConvertStringToDate
        private string ConvertStringToDate(string timespan)
        {
            try
            {
                DateTime dt = new DateTime(1970, 1, 1,12,0,0);
                var date = dt.AddMilliseconds(double.Parse(timespan));

                return date.ToString("yyyy年MM月dd日");
            }
            catch
            {
                return string.Empty;
            }
        }
        #endregion

        #region ConvertProToSim
        private string ConvertProToSim(string city)
        {
            switch(city)
            {
                case "北京市":
                    return "BJ";
                case "天津市":
                    return "TJ";
                case "河北省":
                    return "HB";
                case "山西省":
                    return "SX";
                case "陕西省":
                    return "SHANXI";
                case "辽宁省":
                    return "LN";
                case "吉林省":
                    return "JL";
                case "黑龙江省":
                    return "HLJ";
                case "上海市":
                    return "SH";
                case "江苏省":
                    return "JS";
                case "浙江省":
                    return "ZJ";
                case "安徽省":
                    return "AH";
                case "福建省":
                    return "FJ";
                case "江西省":
                    return "JX";
                case "山东省":
                    return "SD";
                case "河南省":
                    return "HN";
                case "湖北省":
                    return "HUBEI";
                case "湖南省":
                    return "HUNAN";
                case "广东省":
                    return "GD";
                case "贵州省":
                    return "GZ";
                case "广西壮族自治区":
                    return "GX";
                case "海南省":
                    return "HAINAN";
                case "重庆市":
                    return "CQ";
                case "四川省":
                    return "SC";
                case "云南省":
                    return "YN";
                case "西藏自治区":
                    return "XZ";
                case "甘肃省":
                    return "GS";
                case "青海省":
                    return "QH";
                case "宁夏回族自治区":
                    return "NX";
                case "新疆维吾尔自治区":
                    return "XJ";
                default:
                    return "error";
            }
           
        }
        #endregion

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

            _enterpriseInfo.qg_parameters = this.CreateInitParameter();
        }
        #endregion

        #region CreateInitParameter
        /// <summary>
        /// CreateInitParameter
        /// </summary>
        /// <returns></returns>
        Dictionary<string,string> CreateInitParameter()
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
            return dic;
        }
        #endregion

        #region CreateRequest
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

        #region 匹配省份
        /// <summary>
        /// 匹配省份
        /// </summary>
        /// <param name="no"></param>
        /// <returns></returns>
        private string  MatchPro(string no)
        {
            switch(no){
                case "11":
                    return "BJ";
                case "34":
                    return "AH";
                case "50":
                    return "CQ";
                case "35":
                    return "FJ";
                case "62":
                    return "GS";
                case "44":
                    return "GD";
                case "45":
                    return "GX";
                case "52":
                    return "GZ";
                case "46":
                    return "HAINAN";
                case "13":
                    return "HB";
                case "41":
                    return "HN";
	            case "23":
                    return "HLJ";
	            case "42":
                    return "HUBEI";
	            case "43":
                    return "HUNAN";
                case "22":
                    return "JL";
	            case "32":
                    return "JS";
                case "36":
                    return "JX";
                case "21":
                    return "LN";
                case "15":
                    return "NMG";
                case "64":
                    return "NX";
                case "63":
                    return "QH";
                case "37":
                    return "SD";
                case "14":
                    return "SX";
                case "61":
                    return "SHANXI";
                case "31":
                    return "SH";
                case "51":
                    return "SC";
                case "33":
                    return "ZJ"; 
                case "53":
                    return "YN";
                case "65":
                    return "XJ";
                case "54":
                    return "XZ";
                case "12":
                    return "TJ";
                case "10":
                    return "CN";
                default:
                    return "CN";

            }

        }
        #endregion

        #region GetResponseInfo
        /// <summary>
        /// GetResponseInfo
        /// </summary>
        /// <param name="elements"></param>
        /// <returns></returns>
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
    }
}
