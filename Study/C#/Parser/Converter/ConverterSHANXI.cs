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

namespace iOubo.iSpider.Infrastructure.Parser
{
    public class ConverterSHANXI : IConverter
    {
        DataRequest _request;
        RequestInfo _requestInfo;
        RequestXml _requestXml;
        EnterpriseInfo _enterpriseInfo = new EnterpriseInfo(); 
        List<AbnormalInfo> _abnormals = new List<AbnormalInfo>();
        List<CheckupInfo> _checkups = new List<CheckupInfo>();

        bool revokeFlag = false;//是否吊销

        List<string> reportsNeedToLoad = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings.Get("ReportsNeedToLoad"))
? new List<string>() : ConfigurationManager.AppSettings.Get("ReportsNeedToLoad").Split(',').ToList();

        string _enterpriseName = string.Empty;
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

            //解析基本信息：基本信息
            List<ResponseInfo> responseList = GetResponseInfo(_requestXml.GetRequestListByGroup("basic"));
            //替换注册号
            var basicInfo = responseList.Where(p => p.Name == "basic").FirstOrDefault();
            this.LoadAndParseBasicInfo_Only(basicInfo.Data, _enterpriseInfo);
            if (!(requestInfo.Parameters.ContainsKey("platform") && "LIST_API" == requestInfo.Parameters["platform"]))
            {
                Parallel.ForEach(responseList, p => ParallForResponse(p));
            }
            else
            {
                if (this._requestInfo.Parameters.ContainsKey("platform"))
                {
                    this._requestInfo.Parameters.Remove("platform");
                }
                _enterpriseInfo.parameters = this._requestInfo.Parameters;
            }
            SummaryEntity summaryEntity = new SummaryEntity();
            summaryEntity.Enterprise = _enterpriseInfo;
            summaryEntity.Abnormals = _abnormals;
            summaryEntity.Checkups = _checkups;

            return summaryEntity;
        }

        void ParallForResponse(ResponseInfo response)
        {
            switch (response.Name)
            {
                case "basic":
                    Parallel.ForEach(new List<int>(){1,2,3,4,5,6,7,8,9,10,11,12,13},p=>LoadAllBasicDetail(p,response.Data));
                    break;
                case "jingyin":
                    LoadAndParseJingyinNew(response.Data, _enterpriseInfo);
                    break;
                case "punishment":
                    LoadAndParsePunishments(response.Data, _enterpriseInfo);
                    break;
            }

        }

        private void LoadAllBasicDetail(int index,string response)
        {
            switch (index)
            {
                case 1:
                    LoadAndParseBasicNew(response, _enterpriseInfo);
                    break;
                case 2:
                    LoadAndParseEmployeeNew(response, _enterpriseInfo);
                    break;
                case 3:
                    LoadAndParseMortgage(response, _enterpriseInfo);
                    break;
                case 4:
                    LoadAndParseEquity(response, _enterpriseInfo);
                    break;
                case 5:
                    LoadAndParseCheckNew(response, _enterpriseInfo);
                    break;
                case 6:
                    LoadLicense(response, _enterpriseInfo);
                    break;
                case 7:
                    LoadAndParseStockChange(response, _enterpriseInfo);
                    break;
                case 8:
                    LoadAndParseFCAndUR(response, _enterpriseInfo);
                    break;
                case 9:
                    LoadAndParseKnowledge(response, _enterpriseInfo);
                    break;
                case 10:
                    LoadAndParseReportNew(response, _enterpriseInfo);
                    break;
                case 11:
                    LoadSocietyInfo(response, _enterpriseInfo);
                    break;
                case 12:
                    LoadStatisticsInfo(response, _enterpriseInfo);
                    break;
                case 13:
                    LoadAndParseJudicialFreezes(response);
                    break;
            }
        }

        private void InitialEnterpriseInfo()
        {
            _enterpriseInfo.province = this._requestInfo.Province;
            _enterpriseInfo.last_update_time = DateTime.Now;
            _enterpriseInfo.source = "Batch";
           
        }

        private List<ResponseInfo> GetResponseInfo(IEnumerable<XElement> elements)
        {
            List<ResponseInfo> responseList = new List<ResponseInfo>();
            foreach (XElement el in elements)
            {
                responseList.Add(this._request.RequestData(el, "gb2312"));
            }

            return responseList;
        }

        #region 解析司法协助信息
        /// <summary>
        /// 解析司法协助信息
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseJudicialFreezes(string responseData)
        { 
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            List<MortgageInfo> list = new List<MortgageInfo>();
            HtmlNode table = rootNode.SelectSingleNode("//table[@id='table_sfxz']");
            if (table != null)
            {
                var trs = table.SelectNodes("./tr[@name='sfxz']");
                if (trs != null && trs.Any())
                {
                    foreach (var tr in trs)
                    {
                        var tds = tr.SelectNodes("./td");
                        if (tds != null && tds.Count() == 7)
                        {
                            JudicialFreeze jf = new JudicialFreeze();
                            jf.seq_no = _enterpriseInfo.judicial_freezes.Count + 1;
                            jf.be_executed_person = tds[1].InnerText;
                            jf.amount = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ","");
                            jf.executive_court = tds[3].InnerText;
                            jf.number = tds[4].InnerText;
                            jf.status = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(" ", "");
                            jf.type = "股权冻结";
                            var aNode = tds.Last().SelectSingleNode("./a");
                            if (aNode != null && aNode.Attributes.Contains("onclick"))
                            {
                                var request = this.CreateRequest();
                                var arr = aNode.Attributes["onclick"].Value.Split('\'');
                                if (arr[3] == "2")
                                {
                                    jf.type = "股权变更";
                                }
                                request.AddOrUpdateRequestParameter("xh", arr[1]);
                                request.AddOrUpdateRequestParameter("lx", arr[3]);
                                var responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("judicial_freeze_detail"));
                                if (responseList != null && responseList.Any())
                                {
                                    var inner_document = new HtmlDocument();
                                    inner_document.LoadHtml(responseList.First().Data);
                                    HtmlNode inner_rootNode = inner_document.DocumentNode;
                                    var inner_tables = inner_rootNode.SelectNodes("//table");

                                    if (inner_tables != null && inner_tables.Any())
                                    {
                                        foreach (var inner_table in inner_tables)
                                        {
                                            var p = inner_table.SelectSingleNode("./preceding-sibling::p[1]");
                                            if (p.InnerText.Contains("股权冻结信息"))
                                            {
                                                this.LoadAndParseFreezeDetail(jf, inner_table.SelectNodes("./tr"));
                                            }
                                            else if (p.InnerText.Contains("股权解冻信息"))
                                            {
                                                this.LoadAndParseUnFreezeDetail(jf, inner_table.SelectNodes("./tr"));
                                            }
                                            else if (p.InnerText.Contains("股权续行冻结信息") || p.InnerText.Contains("续行冻结信息") || p.InnerText.Contains("股权续行冻结"))
                                            {
                                                this.LoadAndParseContinueFreeze(jf, inner_table.SelectNodes("./tr"));
                                            }
                                            else if (p.InnerText.Contains("股东变更信息") || p.InnerText.Contains("股权变更信息"))
                                            {
                                                jf.type = "股权变更";
                                                this.LoadAndParsePartnerChangeFreeze(jf, inner_table.SelectNodes("./tr"));
                                            }
                                        }
                                    }
                                }
                            }
                            _enterpriseInfo.judicial_freezes.Add(jf);
                        }
                    }
                    
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

        #region 解析股东及出资信息
        /// <summary>
        /// 解析股东及出资信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseFCAndUR(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            List<FinancialContribution> _FinancialList = new List<FinancialContribution>();//股东出资
            List<UpdateRecord> updateRecords = new List<UpdateRecord>();
            LoadAndParseFinancialContribution(rootNode.SelectSingleNode("//table[@id='table_qytzr']"), _FinancialList);
           // LoadAndParseUpdatedRecords(rootNode.SelectSingleNode("//table[@id='table_tzrxxbg']"), updateRecords);
            _enterpriseInfo.financial_contributions = _FinancialList;
            //_enterpriseInfo.update_records = updateRecords;
        }

        private void LoadAndParseFinancialContribution(HtmlNode table, List<FinancialContribution> _FinancialList)
        {
            if (table != null)
            {
                var heardRows = table.SelectNodes("./tr");
                if (heardRows != null && heardRows.Count > 0)
                {
                    HtmlNodeCollection trList = table.SelectNodes("./tr");
                    if (trList != null && trList.Count > 3)
                    {
                        for (int i = 3; i < trList.Count; i++)
                        {
                            FinancialContribution item = new FinancialContribution();
                            HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                            if (tdList != null && tdList.Count > 8)
                            {
                                item.seq_no = _FinancialList.Count + 1;
                                item.investor_name = RemoveUnexceptedChar(tdList[0].InnerText);
                                item.total_should_capi = RemoveUnexceptedChar(tdList[1].InnerText);
                                item.total_real_capi = RemoveUnexceptedChar(tdList[2].InnerText);
                                List<FinancialContribution.ShouldCapiItem> scList = new List<FinancialContribution.ShouldCapiItem>();
                                FinancialContribution.ShouldCapiItem sc = new FinancialContribution.ShouldCapiItem();
                                sc.should_invest_type = RemoveUnexceptedChar(tdList[3].InnerText);
                                sc.should_capi = RemoveUnexceptedChar(tdList[4].InnerText);
                                sc.should_invest_date = RemoveUnexceptedChar(tdList[5].InnerText);
                                sc.public_date = RemoveUnexceptedChar(tdList[6].InnerText);
                                scList.Add(sc);
                                item.should_capi_items = scList;
                                List<FinancialContribution.RealCapiItem> rcList = new List<FinancialContribution.RealCapiItem>();
                                FinancialContribution.RealCapiItem rc = new FinancialContribution.RealCapiItem();
                                rc.real_invest_type = RemoveUnexceptedChar(tdList[7].InnerText);
                                rc.real_capi = RemoveUnexceptedChar(tdList[8].InnerText);
                                rc.real_invest_date = RemoveUnexceptedChar(tdList[9].InnerText);
                                rc.public_date = RemoveUnexceptedChar(tdList[10].InnerText);
                                rcList.Add(rc);
                                item.real_capi_items = rcList;
                                _FinancialList.Add(item);
                            }
                        }
                    }

                }
            }
        }
        #endregion

        /// <summary>
        /// 解析动产抵押
        /// </summary>
        /// <param name="partner"></param>
        /// <param name="responseData"></param>
        private void LoadAndParseMortgage(String responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            RequestHandler request = new RequestHandler();

            List<MortgageInfo> list = new List<MortgageInfo>();
            HtmlNode table = rootNode.SelectSingleNode("//table[@id='table_dcdy']");
            if (table != null)
            {
                HtmlNodeCollection rows = table.SelectNodes("./tr");
                foreach (HtmlNode rowNode in rows)
                {
                    HtmlNodeCollection cells = rowNode.SelectNodes("./td");

                    if (cells != null && cells.Count > 6)
                    {
                        MortgageInfo item = new MortgageInfo();
                        item.seq_no = list.Count + 1;
                        item.number = RemoveUnexceptedChar(cells[1].InnerText);
                        item.date = RemoveUnexceptedChar(cells[2].InnerText);
                        item.department = RemoveUnexceptedChar(cells[3].InnerText);
                        item.amount = RemoveUnexceptedChar(cells[4].InnerText);
                        item.status = RemoveUnexceptedChar(cells[5].InnerText);
                        item.public_date = RemoveUnexceptedChar(cells[6].InnerText);
                        int startIndex = cells[7].InnerHtml.IndexOf("('");
                        int endIndex = cells[7].InnerHtml.IndexOf("')");
                        if (startIndex > 0 && endIndex > 0)
                        {
                            var id = cells[7].InnerHtml.Substring(startIndex + 2, endIndex - startIndex - 2);
                            var request2 = CreateRequest();
                            request2.AddOrUpdateRequestParameter("xh", id);
                            var response = request2.GetResponseInfo(new[] { _requestXml.GetRequestItemByName("dongchandiyadetail") });
                            if (response != null && response.Count > 0)
                            {
                                LoadMortgageDetail(response[0].Data, item);
                            }
                        }
                        list.Add(item);
                    }
                }
            }
            _enterpriseInfo.mortgages = list;
        }

        void LoadSocietyInfo(string data, EnterpriseInfo _enterpriseInfo)
        {
            
            //HumanSocietyInfo info = new HumanSocietyInfo();
            //HtmlDocument doc = new HtmlDocument();
            //doc.LoadHtml(data);
            //var nodes = doc.DocumentNode.SelectNodes("//div[@class='part']");
            //foreach (var node in nodes)
            //{
            //    if (node.SelectSingleNode("./p") != null && node.SelectSingleNode("./p").InnerText.Trim() == "人社部门信息")
            //    {
            //        var trList = node.SelectNodes("./table/tr");
            //        if (trList != null && trList.Count > 1)
            //        {
            //            for (int i = 0; i < trList.Count; i++)
            //            {
            //                HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
            //                HtmlNodeCollection thList = trList[i].SelectNodes("./th");
            //                for (int j = 0; j < tdList.Count; j++)
            //                {
            //                    switch (thList[j].InnerText.Trim())
            //                    {
            //                        case "注册号":
            //                            info.reg_no = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "统一社会信用代码":
            //                            info.org_no = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "企业名称":
            //                            info.name = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "企业地址":
            //                            info.address = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "企业电话":
            //                            info.phone = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "企业邮编":
            //                            info.postcode = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "法定代表人":
            //                            info.oper_name = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "法定代表人证件号码":
            //                            info.oper_id = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "法定代表人电话":
            //                            info.oper_phone = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "单位经济类型":
            //                            info.economic_type = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "行政区划":
            //                            info.administrative_divisio = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "参加社保险种":
            //                            info.insurance_type = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "参加社保人数":
            //                            info.insurance_persons = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "参加社保日期":
            //                            info.insurance_date = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                    }
            //                }
            //            }
            //        }

            //    }
            //}
            //_enterpriseInfo.human_society_info = new List<HumanSocietyInfo>() {info };
        }

        void LoadStatisticsInfo(string data, EnterpriseInfo _enterpriseInfo)
        {
            //StatisticsDepartmentInfo info = new StatisticsDepartmentInfo();
            //HtmlDocument doc = new HtmlDocument();
            //doc.LoadHtml(data);
            //var nodes = doc.DocumentNode.SelectNodes("//div[@class='part']");
            //foreach (var node in nodes)
            //{
            //    if (node.SelectSingleNode("./p") != null && node.SelectSingleNode("./p").InnerText.Trim() == "统计部门信息")
            //    {
            //        var trList = node.SelectNodes("./table/tr");

            //        if (trList != null && trList.Count > 1)
            //        {
            //            for (int i = 0; i < trList.Count; i++)
            //            {
            //                HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
            //                HtmlNodeCollection thList = trList[i].SelectNodes("./th");
            //                for (int j = 0; j < tdList.Count; j++)
            //                {
            //                    switch (thList[j].InnerText.Trim())
            //                    {
            //                        case "注册号":
            //                            info.reg_no = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "组织机构代码":
            //                            info.org_no = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "统一社会信用代码":
            //                            info.credit_no = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "企业名称":
            //                            info.name = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "企业地址":
            //                            info.address = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "联系方式":
            //                            info.phone = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "法定代表人":
            //                            info.oper_name = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "主要业务活动1":
            //                            info.main_activity1 = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "主要业务活动2":
            //                            info.main_activity2 = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "主要业务活动3":
            //                            info.main_activity3 = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "行业代码":
            //                            info.area_code = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "单位所在地区划代码":
            //                            info.area_code = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "登记注册类型":
            //                            info.econ_type = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "营业状态":
            //                            info.status = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                        case "控股情况":
            //                            info.stake_info = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
            //                            break;
            //                    }
            //                }
            //            }

            //        }
            //    }
            //}
            //_enterpriseInfo.statics_depart_info = new List<StatisticsDepartmentInfo>() { info };
        }

        private string RemoveUnexceptedChar(string str)
        {
            int index = str.IndexOf("-->");
            if (index > -1)
            {
                str = str.Substring(index + 3);
            }
            return str.Replace("\r\n", "").Replace("\t", "").Replace("&nbsp;", "").Trim();
        }


        private void LoadMortgageDetail(string responseData, MortgageInfo mortgage)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);

            HtmlNode rootNode = document.DocumentNode;
            HtmlNodeCollection infoTable = rootNode.SelectNodes("//table");
            if (infoTable != null)
            {
                foreach (HtmlNode table in infoTable)
                {
                    var headerRow = table.SelectNodes("./tr");
                    if (headerRow == null || headerRow.Count == 0)
                        return;
                    string header = headerRow[0].InnerText.Trim();
                    HtmlNodeCollection trList = table.SelectNodes("./tr");
                    if (header.Contains("种类"))
                    {
                        if (trList != null && trList.Count > 1)
                        {
                            for (int i = 0; i < trList.Count; i++)
                            {
                                HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                                HtmlNodeCollection thList = trList[i].SelectNodes("./th");
                                for (int j = 0; j < tdList.Count; j++)
                                {
                                    switch (thList[j].InnerText.Trim())
                                    {
                                        case "种类":
                                            mortgage.debit_type = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
                                            break;
                                        case "数额":
                                            mortgage.debit_amount = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
                                            break;
                                        case "担保的范围":
                                            mortgage.debit_scope = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
                                            break;
                                        case "债务人履行债务的期限":
                                            mortgage.debit_period = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
                                            break;
                                        case "备注":
                                            mortgage.debit_remarks = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
                                            break;
                                    }
                                }
                            }
                        }
                    }
                    else if (header.Contains("抵押权人名称"))
                    {
                        List<Mortgagee> mortgagees = new List<Mortgagee>();
                        if (trList != null && trList.Count >= 1)
                        {
                            for (int i = 1; i < trList.Count; i++)
                            {
                                Mortgagee mortgagee = new Mortgagee();
                                HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                                if (tdList != null && tdList.Count > 3)
                                {
                                    mortgagee.seq_no = int.Parse(tdList[0].InnerText.Trim());
                                    mortgagee.name = tdList[1].InnerText.Trim();
                                    mortgagee.identify_type = tdList[2].InnerText.Trim();
                                    mortgagee.identify_no = tdList[3].InnerText.Trim();
                                    mortgagees.Add(mortgagee);
                                }
                            }

                        }
                        mortgage.mortgagees = mortgagees;
                    }
                    else if (header.Contains("所有权或使用权归属"))
                    {
                        List<Guarantee> guarantees = new List<Guarantee>();
                        if (trList != null && trList.Count >= 1)
                        {
                            for (int i = 1; i < trList.Count; i++)
                            {
                                Guarantee guarantee = new Guarantee();
                                HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                                if (tdList != null && tdList.Count > 4)
                                {
                                    guarantee.seq_no = guarantees.Count + 1;
                                    guarantee.name = RemoveUnexceptedChar(tdList[1].InnerText.Trim());
                                    guarantee.belong_to = RemoveUnexceptedChar(tdList[2].InnerText.Trim());
                                    guarantee.desc = RemoveUnexceptedChar(tdList[3].InnerText.Trim());
                                    guarantee.remarks = RemoveUnexceptedChar(tdList[4].InnerText.Trim());
                                    guarantees.Add(guarantee);
                                }
                            }
                        }
                        mortgage.guarantees = guarantees;
                    }
                }
            }



        }

        private void LoadLicense(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            #region 行政许可信息
            HtmlNode table = rootNode.SelectSingleNode("//table[@id='table_xzxk']");
            if (table != null)
            {
                HtmlNodeCollection trList = table.SelectNodes("./tr");
                if (trList != null && trList.Count > 0)
                {
                    List<LicenseInfo> licenseList = new List<LicenseInfo>();
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                        if (tdList != null && tdList.Count > 8)
                        {
                            LicenseInfo license = new LicenseInfo();
                            license.seq_no = licenseList.Count + 1;
                            license.number = RemoveUnexceptedChar(tdList[1].InnerText);
                            license.name = RemoveUnexceptedChar(tdList[2].InnerText);
                            license.start_date = RemoveUnexceptedChar(tdList[3].InnerText);
                            license.end_date = RemoveUnexceptedChar(tdList[4].InnerText);
                            license.department = RemoveUnexceptedChar(tdList[5].InnerText);
                            license.content = tdList[6].SelectSingleNode("./input[@type='hidden']") != null ?
                                tdList[6].SelectSingleNode("./input[@type='hidden']").Attributes["value"].Value : string.Empty;
                            license.status = RemoveUnexceptedChar(tdList[7].InnerText);
                            licenseList.Add(license);
                        }
                    }
                    _enterpriseInfo.licenses = licenseList;
                }
            }
            #endregion
        }


        private void LoadAndParseStockChange(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            List<StockChangeItem> stocks = new List<StockChangeItem>();
            HtmlNode stockTable = rootNode.SelectSingleNode("//table[@id='table_tzrbgxx']");
            if (stockTable != null)
            {
                HtmlNodeCollection stockRows = stockTable.SelectNodes("./tr");
                foreach (HtmlNode rowNode in stockRows)
                {
                    HtmlNodeCollection stockCells = rowNode.SelectNodes("./td");
                    if (stockCells != null && stockCells.Count > 4)
                    {
                        StockChangeItem stock = new StockChangeItem();
                        stock.seq_no = stocks.Count + 1;
                        stock.name = stockCells[1].InnerText;
                        stock.before_percent = stockCells[2].InnerText;
                        stock.after_percent = stockCells[3].InnerText;
                        stock.change_date = stockCells[4].InnerText;
                        stocks.Add(stock);
                    }
                }
            }
            _enterpriseInfo.stock_changes = stocks;
        }

        private void LoadAndParseKnowledge(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            List<KnowledgeProperty> knowledges = new List<KnowledgeProperty>();
            HtmlNode knowledgesTable = rootNode.SelectSingleNode("//table[@id='table_zscq']");
            if (knowledgesTable != null)
            {
                HtmlNodeCollection knowledgesRows = knowledgesTable.SelectNodes("./tr");
                foreach (HtmlNode rowNode in knowledgesRows)
                {
                    HtmlNodeCollection stockCells = rowNode.SelectNodes("./td");
                    if (stockCells != null && stockCells.Count > 4)
                    {
                        KnowledgeProperty property = new KnowledgeProperty();
                        property.seq_no = int.Parse(stockCells[0].InnerText.Trim());
                        property.number = stockCells[1].InnerText.Trim();
                        property.name = stockCells[2].InnerText.Trim();
                        property.type = stockCells[3].InnerText.Trim();
                        property.pledgor = stockCells[4].InnerText.Trim();
                        property.pawnee = stockCells[5].InnerText.Trim();
                        property.period = stockCells[6].InnerText.Trim();
                        property.status = stockCells[7].InnerText.Trim();
                        property.public_date = stockCells[8].InnerText.Trim();
                        knowledges.Add(property);
                    }
                }
            }
            _enterpriseInfo.knowledge_properties = knowledges;
        }




        /// <summary>
        /// 股权出质
        /// </summary>
        /// <param name="responseInfo"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseEquity(String responseInfo, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseInfo);
            HtmlNode rootNode = document.DocumentNode;

            List<EquityQuality> list = new List<EquityQuality>();
            HtmlNode table = rootNode.SelectSingleNode("//table[@id='table_gqcz']");
            if (table != null)
            {
                HtmlNodeCollection rows = table.SelectNodes("./tr");
                foreach (HtmlNode rowNode in rows)
                {
                    HtmlNodeCollection cells = rowNode.SelectNodes("./td");
                    if (cells != null && cells.Count > 9)
                    {
                        EquityQuality item = new EquityQuality();
                        item.seq_no = list.Count + 1;
                        item.number = RemoveUnexceptedChar(cells[1].InnerText);
                        item.pledgor = RemoveUnexceptedChar(cells[2].InnerText);
                        item.pledgor_identify_no = RemoveUnexceptedChar(cells[3].InnerText);
                        item.pledgor_amount = RemoveUnexceptedChar(cells[4].InnerText);
                        item.pawnee = RemoveUnexceptedChar(cells[5].InnerText);
                        item.pawnee_identify_no = RemoveUnexceptedChar(cells[6].InnerText);
                        item.date = RemoveUnexceptedChar(cells[7].InnerText);
                        item.status = RemoveUnexceptedChar(cells[8].InnerText);
                        item.public_date = RemoveUnexceptedChar(cells[9].InnerText);
                        list.Add(item);
                    }
                }
            }
            _enterpriseInfo.equity_qualities = list;

        }

        void LoadAndParseBasicInfo_Only(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            //基本信息
            var tbbasic = rootNode.SelectSingleNode("//table[@class='table_xq']");
            if (tbbasic != null)
            {
                HtmlNodeCollection tdList = tbbasic.SelectNodes("./tr/td");
                for (int i = 1; i < tdList.Count; i++)
                {
                    switch (tdList[i].InnerText.Split('：', ':')[0].Replace("&nbsp;", "").Replace("·", "").Trim())
                    {
                        case "注册号":
                            _enterpriseInfo.reg_no = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                            break;
                        case "统一社会信用代码":
                            _enterpriseInfo.credit_no = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                            break;
                        case "注册号/统一社会信用代码":
                        case "统一社会信用代码/注册号":
                            if (tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "").Length == 18)
                                _enterpriseInfo.credit_no = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                            else
                                _enterpriseInfo.reg_no = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                            break;
                        case "名称":
                        case "企业名称":
                            _enterpriseInfo.name = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "").Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                            break;
                        case "类型":
                            _enterpriseInfo.econ_kind = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "").Replace("null", "").Replace("NULL", "");
                            break;
                        case "法定代表人":
                        case "负责人":
                        case "股东":
                        case "经营者":
                        case "执行事务合伙人":
                        case "投资人":
                            _enterpriseInfo.oper_name = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "").Replace("null", "").Replace("NULL", "");
                            break;
                        case "住所":
                        case "经营场所":
                        case "营业场所":
                            Address address = new Address();
                            address.name = "注册地址";
                            address.address = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                            address.postcode = "";
                            _enterpriseInfo.addresses.Add(address);
                            break;
                        case "注册资金":
                        case "注册资本":
                        case "成员出资总额":
                            _enterpriseInfo.regist_capi = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                            break;
                        case "成立日期":
                        case "登记日期":
                        case "注册日期":
                            _enterpriseInfo.start_date = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                            break;
                        case "营业期限自":
                        case "经营期限自":
                        case "合伙期限自":
                            _enterpriseInfo.term_start = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                            break;
                        case "营业期限至":
                        case "经营期限至":
                        case "合伙期限至":
                            _enterpriseInfo.term_end = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                            break;
                        case "经营范围":
                        case "业务范围":
                            _enterpriseInfo.scope = tdList[i].InnerText.Replace("经营范围：", "").Replace("业务范围：", "").Replace("·", "").Replace("null", "").Replace("NULL", "").Replace("&nbsp;", "").Trim();
                            break;
                        case "登记机关":
                            _enterpriseInfo.belong_org = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                            break;
                        case "核准日期":
                            _enterpriseInfo.check_date = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                            break;
                        case "登记状态":
                            _enterpriseInfo.status = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                            break;
                        case "吊销日期":
                        case "注销日期":
                            _enterpriseInfo.end_date = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                            break;
                        case "组成形式":
                            _enterpriseInfo.type_desc = tdList[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                            break;
                        default:
                            break;
                    }
                }

                if (_enterpriseInfo.status.Contains("吊销"))
                {
                    revokeFlag = true;
                    return;
                }


            }
            if (string.IsNullOrWhiteSpace(_enterpriseName))
            {
                _enterpriseName = _enterpriseInfo.name;
            }
        }
        #region 解析工商公示信息：基本信息、股东信息、变更信息
        private void LoadAndParseBasicNew(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            var tbbasic = rootNode.SelectSingleNode("//table[@class='detailsList']");
            //解析股东信息
            var tbgd = rootNode.SelectSingleNode("//table[@id='table_fr']");
            if (tbgd != null)
            {
                RequestHandler request = new RequestHandler();
                request.ResponseEncoding = "gb2312";
                HtmlNodeCollection trList = tbgd.SelectNodes("//tr[@name='fr']");
                if (trList != null && trList.Any())
                {
                    foreach (HtmlNode trNode in trList)
                    {
                        var tdList = trNode.ChildNodes.Where(p => p.Name == "td").ToList();
                        var partner = new Partner();//股东
                        partner.seq_no = _enterpriseInfo.partners.Count + 1;
                        if (tdList.Count == 2)
                        {
                            partner.stock_name = tdList[0].InnerText.Replace("\r", "").Replace("\n", "").Trim();
                            partner.identify_type = tdList[1].InnerText.Replace("\r", "").Replace("\n", "").Trim();
                            _enterpriseInfo.partners.Add(partner);
                        }
                        else if (tdList.Count == 3)
                        {
                            partner.stock_name = tdList[1].InnerText.Replace("\r", "").Replace("\n", "").Trim();
                            partner.identify_type = tdList[2].InnerText.Replace("\r", "").Replace("\n", "").Trim();
                            _enterpriseInfo.partners.Add(partner);
                        }
                        else
                        {
                            if (tdList.Count > 3)
                            {
                                partner.identify_no = tdList[4].InnerText.Replace("\r", "").Replace("\n", "").Trim();
                            }
                            partner.stock_name = tdList[1].InnerText.Replace("\r", "").Replace("\n", "").Trim();
                            partner.identify_type = tdList[3].InnerText.Replace("\r", "").Replace("\n", "").Trim();
                            if (tdList.Count > 1)
                            {
                                partner.stock_type = tdList[2].InnerText.Replace("\r", "").Replace("\n", "").Trim();
                            }
                            if (tdList.Count > 5)
                            {
                                //获取详情信息
                                var btnDetailA = tdList[5].ChildNodes.FirstOrDefault(p => p.Name == "a");
                                if (btnDetailA != null)
                                {
                                    var clickInfo = btnDetailA.Attributes["onclick"].Value;
                                    var clickInfoArr = clickInfo.Replace("showRyxx(", "").Replace(")", "").Replace("'", "").Split(',');
                                    if (clickInfoArr.Length >= 2)
                                    {
                                        var detailId = clickInfoArr[0];
                                        var regNo = clickInfoArr[1];
                                        var detailParams = string.Format("method={0}&maent.xh={1}&maent.pripid={2}&random={3}", "frInfoDetail", detailId, regNo, DateTime.Now.Ticks);
                                        var responseStr = request.HttpGet("http://sn.gsxt.gov.cn/ztxy.do", detailParams);
                                        var tuple = GetPartnerDetail(responseStr,partner);
                                        partner.should_capi_items = tuple.Item1;
                                        partner.real_capi_items = tuple.Item2;
                                    }
                                }

                            }
                            _enterpriseInfo.partners.Add(partner);
                        }
                    }
                }

            }
            //解析变更信息
            var tbbg = rootNode.SelectSingleNode("//table[@id='table_bgxx']");
            if (tbbg != null)
            {
                HtmlNodeCollection trList = tbbg.SelectNodes("./tr[@name='bgxx']");
                if (trList != null && trList.Any())
                {
                    foreach (HtmlNode trNode in trList)
                    {
                        var tdList = trNode.ChildNodes.Where(p => p.Name == "td").ToList();
                        var changeRecord = new ChangeRecord();//信息变更
                        changeRecord.change_item = tdList[1].InnerText.Trim(new char[] { '\n', ' ' });
                        changeRecord.before_content = tdList[2].InnerText.Trim(new char[] { '\n', ' ' });
                        changeRecord.after_content = tdList[3].InnerText.Trim(new char[] { '\n', ' ' });
                        changeRecord.change_date = tdList[4].InnerText.Trim(new char[] { '\n', ' ' });
                        _enterpriseInfo.changerecords.Add(changeRecord);
                    }
                }
            }
        }
        #endregion

        #region 解析股东详情信息
        public Tuple<List<ShouldCapiItem>, List<RealCapiItem>> GetPartnerDetail(string responseStr,Partner partner)
        {
            var sciList = new List<ShouldCapiItem>();
            var rciList = new List<RealCapiItem>();
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseStr);
            HtmlNode rootNode = document.DocumentNode;
            var tbs = rootNode.SelectNodes("//table[@class='table_list']");
            string totalShould = "";
            string totalReal = "";
            foreach (var table in tbs)
            {
                if (table.FirstChild.NextSibling != null && table.FirstChild.NextSibling.InnerText.Contains("股东名称"))
                {
                    HtmlNodeCollection tdList = table.SelectNodes("./tr/td");
                    HtmlNodeCollection thList = table.SelectNodes("./tr/th");
                    for (int j = 0; j < tdList.Count; j++)
                    {
                        switch (thList[j].InnerText.Trim())
                        {
                            case "认缴额（万元）":
                                totalShould = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
                                break;
                            case "实缴额（万元）":
                                totalReal = RemoveUnexceptedChar(tdList[j].InnerText.Trim());
                                break;

                        }
                    }
                }
                else if (table.FirstChild.NextSibling != null && table.FirstChild.NextSibling.InnerText.Contains("认缴出资方式"))
                {

                    var trList = table.SelectNodes("./tr");
                    if (trList.Count > 1)
                    {
                        for (int index = 1; index < trList.Count; index++)
                        {
                            var tdList = table.SelectNodes("./tr")[index].SelectNodes("./td");
                            var sci = new ShouldCapiItem()
                        {
                            shoud_capi = tdList[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("&nbsp;", "").Trim(),
                            invest_type = tdList[0].InnerText.Replace("\r", "").Replace("\n", "").Replace("&nbsp;", "").Trim(),
                            should_capi_date = tdList[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("&nbsp;", "").Trim()
                        };
                            sciList.Add(sci);
                        }
                    }
                }
                else if (table.FirstChild.NextSibling != null && table.FirstChild.NextSibling.InnerText.Contains("实缴出资方式"))
                {
                    var trList = table.SelectNodes("./tr");
                    if (trList.Count > 1)
                    {
                        for (int index = 1; index < trList.Count; index++)
                        {
                            var tdList = table.SelectNodes("./tr")[index].SelectNodes("./td");
                            var rci = new RealCapiItem()
                            {
                                real_capi = tdList[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("&nbsp;", "").Trim(),
                                invest_type = tdList[0].InnerText.Replace("\r", "").Replace("\n", "").Replace("&nbsp;", "").Trim(),
                                real_capi_date = tdList[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("&nbsp;", "").Trim()
                            };

                            rciList.Add(rci);
                        }
                    }
                }
            }
            partner.total_should_capi = totalShould;
            partner.total_real_capi = totalReal;
            return Tuple.Create(sciList, rciList);
        }
        #endregion
       


        private void LoadAndParseEmployeeNew(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            var tbEmployee = rootNode.SelectSingleNode("//ul[@class='person_list']");//主要人员信息
            if (tbEmployee != null)
            {
                var trEmployees = tbEmployee.SelectNodes("./li");
                foreach(var trEmployee in trEmployees)
                {
                    var name = trEmployee.SelectSingleNode("./p[@class='name']");
                    var title = trEmployee.SelectSingleNode("./p[@class='position']");
                       Employee employee1 = new Employee()
                        {

                            seq_no = _enterpriseInfo.employees.Count+1,
                            name = name != null ?name.InnerText.Replace("\r", "").Replace("\n", "").Trim():string.Empty,
                            job_title = title != null ? title.InnerText.Replace("\r", "").Replace("\n", "").Trim() : string.Empty,
                            cer_no = "",
                            sex = "",
                        };
                        _enterpriseInfo.employees.Add(employee1);
                }
            }

            var tbBranch = rootNode.SelectSingleNode("//ul[@class='fzjgxx_list']");//主要人员信息
            if (tbBranch != null)
            {
                var trBranches = tbBranch.SelectNodes("./li");
                foreach (var trBranch in trBranches)
                {
                    var name = trBranch.SelectSingleNode("./p[@class='span1']");
                    var spans2= trBranch.SelectNodes("./p[@class='span2']");
                    Branch branch = new Branch()
                        {

                            seq_no = _enterpriseInfo.branches.Count + 1,
                            name = name != null ? name.Attributes["title"].Value.Replace("\r", "").Replace("\n", "").Trim() : string.Empty,
                            reg_no = spans2 != null ? spans2[0].Attributes["title"].Value.Replace("\r", "").Replace("\n", "").Trim() : string.Empty,
                             belong_org = spans2 != null&&spans2.Count>1 ?spans2[1].InnerText.Split('：')[1]:string.Empty
                        };
                    _enterpriseInfo.branches.Add(branch);
                }
            }
        }
        /// <summary>
        /// 加载解析分支机构
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseBranch(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            List<Branch> branchList = new List<Branch>();
            BranchSHANXI branchHN = JsonConvert.DeserializeObject<BranchSHANXI>(responseData);
            int i = 1;
            foreach (BranchJsonSHANXI branchJson in branchHN.list)
            {
                Branch branch = new Branch();
                branch.seq_no = i++;
                branch.belong_org = branchJson.regorgLabel;
                branch.name = branchJson.brname;
                branch.oper_name = branchJson.prilname;
                branch.reg_no = branchJson.regno;
                branchList.Add(branch);
            }
            _enterpriseInfo.branches = branchList;
        }


      
        private void LoadAndParseJingyinNew(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            List<AbnormalInfo> list = new List<AbnormalInfo>();

            var tbAbnormal = rootNode.SelectSingleNode("//table[@id='table_jyyc']");//主要人员信息
            if (tbAbnormal != null)
            {
                var trAbnormals = tbAbnormal.SelectNodes("./tr[@name='jyyc']");
                if (trAbnormals != null && trAbnormals.Any())
                {
                    foreach (var trEmployee in trAbnormals)
                    {

                        var tdAbnormals = trEmployee.ChildNodes.Where(p => p.Name == "td").ToList();
                        AbnormalInfo abnormalInfo = new AbnormalInfo()
                        {
                            name = _enterpriseInfo.name,
                            reg_no = _enterpriseInfo.reg_no,
                            province = _enterpriseInfo.province,
                            in_reason = tdAbnormals[1].InnerText.Replace("\r", "").Replace("\n", "").Replace("<!-- xd.liu 2014-09-04 17:53 异常移除事由取编码表 -->", "").Trim(),
                            in_date =tdAbnormals[2].InnerText.Replace("\r", "").Replace("\n", "").Trim(),
                            out_reason = tdAbnormals[4].InnerText.Replace("\r", "").Replace("\n", "").Replace("<!-- xd.liu 2014-09-04 17:53 异常移除事由取编码表 -->","").Trim(),
                            out_date = tdAbnormals[5].InnerText.Replace("\r", "").Replace("\n", "").Trim(),
                            department = tdAbnormals[3].InnerText.Replace("\r", "").Replace("\n", "").Trim()
                        };

                        list.Add(abnormalInfo);
                        _abnormals = list;
                    }
                }
            }
        }

        private void LoadAndParsePunishments(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            List<AbnormalInfo> list = new List<AbnormalInfo>();

            var tbPun = rootNode.SelectSingleNode("//table[@id='table_xzcf']");//主要人员信息
            if (tbPun != null)
            {
                var trPuns = tbPun.SelectNodes("./tr[@name='xzcf']");
                if (trPuns != null && trPuns.Any())
                {
                    foreach (var trPun in trPuns)
                    {

                        var tdList = trPun.ChildNodes.Where(p => p.Name == "td").ToList();
                        AdministrativePunishment item = new AdministrativePunishment()
                        {
                            seq_no = int.Parse(tdList[0].InnerText.Trim()),
                            number = tdList[1].InnerText.Trim(),
                            illegal_type = tdList[2].SelectSingleNode("./input[@type='hidden']")==null?tdList[2].InnerText.Trim():
                            tdList[2].SelectSingleNode("./input[@type='hidden']").Attributes["value"].Value,
                            content = tdList[3].SelectSingleNode("./input[@type='hidden']") == null ? tdList[3].InnerText.Trim() :
                            tdList[3].SelectSingleNode("./input[@type='hidden']").Attributes["value"].Value,
                            department = tdList[4].InnerText.Trim(),
                            date = tdList[5].InnerText.Trim(),
                             public_date = tdList[6].InnerText.Trim(),
                            //remark = tdList[6].InnerText.Trim(),
                            name = _enterpriseInfo.name,
                            reg_no = _enterpriseInfo.reg_no,
                            oper_name = _enterpriseInfo.oper_name
                        };

                        var node = tdList[7].SelectSingleNode("./a");
                        if(node!=null)
                        {
                            var value = node.Attributes["onclick"].Value.Replace("doXzfyDetail(", "").Replace(")", "").Replace("'", "").Replace(";", "");
                            var ids = value.Split(',');
                            if (ids.Count() > 1)
                            {
                                var request2 = CreateRequest();
                                request2.AddOrUpdateRequestParameter("xh", ids[1]);
                                var response = request2.GetResponseInfo(new[] { _requestXml.GetRequestItemByName("punishmentdetail") });
                                if (response != null && response.Count > 0)
                                {
                                    HtmlDocument doc = new HtmlDocument();
                                    doc.LoadHtml(response[0].Data);
                                    var remarks = doc.DocumentNode.SelectSingleNode("//div[@class='table_list']");
                                    item.description = remarks.InnerHtml;
                                }
                            }
                        }
                        Utility.ClearNullValue<AdministrativePunishment>(item);
                        _enterpriseInfo.administrative_punishments.Add(item);
                    }
                }
            }
        }
        /// <summary>
        /// 加载解析抽查检查信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseCheck(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            List<CheckupInfo> list = new List<CheckupInfo>();
            CheckSHANXI checkSX = JsonConvert.DeserializeObject<CheckSHANXI>(responseData);
            int i = 1;
            foreach (CheckJsonSHANXI checkJson in checkSX.list)
            {
                CheckupInfo item = new CheckupInfo();
                item.name = _enterpriseInfo.name;
                item.reg_no = _enterpriseInfo.reg_no;
                item.province = _enterpriseInfo.province;
                item.department = checkJson.insAuthString;
                item.type = checkJson.insTypeString;
                item.date = checkJson.insDateString;
                item.result = checkJson.insResString;

                list.Add(item);

            }
            _checkups = list;
        }
        private void LoadAndParseCheckNew(string responseData, EnterpriseInfo _enterpriseInfo)  
        {
            
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            List<CheckupInfo> list = new List<CheckupInfo>();

            var tbCheckups = rootNode.SelectSingleNode("//table[@id='table_ccjc']");//主要人员信息
            if (tbCheckups != null)
            {
                var trCheckups = tbCheckups.SelectNodes("./tr[@name='ccjc']");
                if (trCheckups != null && trCheckups.Any())
                {
                    foreach (var trEmployee in trCheckups)
                    {

                        var tdCheckups = trEmployee.ChildNodes.Where(p => p.Name == "td").ToList();
                        CheckupInfo checkupInfo = new CheckupInfo()
                        {
                            name = _enterpriseInfo.name,
                            reg_no = _enterpriseInfo.reg_no,
                            province = _enterpriseInfo.province,

                            department =tdCheckups[1].InnerText.Replace("\r", "").Replace("\n", "").Trim(),
                            type = tdCheckups[2].InnerText.Replace("\r", "").Replace("\n", "").Trim(),
                            date = tdCheckups[3].InnerText.Replace("\r", "").Replace("\n", "").Trim(),
                            result = tdCheckups[4].InnerText.Replace("\r", "").Replace("\n", "").Trim()

                        };

                        list.Add(checkupInfo);
                        _checkups = list;
                    }
                }
            }
            
        }
        /// <summary>
        /// 解析年报
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseReport(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            List<Report> reportList = new List<Report>();
            HtmlNode div = rootNode.SelectSingleNode("//div[@id='qiyenianbao']");
            if (div != null)
            {
                HtmlNode table = div.SelectSingleNode("./table");
                if (table != null)
                {
                    HtmlNodeCollection trList = table.SelectNodes("./tr");
                    foreach (HtmlNode rowNode in trList)
                    {
                        HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                        if (tdList != null && tdList.Count > 2)
                        {
                            Report report = new Report(); 
                            string id = "";
                            string reportHerf = "";
                            string year = "";
                            if (tdList[1].Element("a") != null)
                            {
                                reportHerf = tdList[1].Element("a").Attributes["href"].Value;
                                id = Regex.Split(Regex.Split(reportHerf, "id=")[1], "&")[0];
                                year = Regex.Split(reportHerf, "nd=")[1];
                            }
                            
                            report.report_name = tdList[1].InnerText.Trim();
                            report.report_year = string.IsNullOrEmpty(year) ? tdList[1].InnerText.Trim().Substring(0, 4) : year;
                            report.report_date = tdList[2].InnerText.Trim();
                            if (reportsNeedToLoad.Count == 0 || reportsNeedToLoad.Contains(report.report_year))
                            {
                                // 加载解析年报详细信息
                                var request = CreateRequest();
                                request.AddOrUpdateRequestParameter("reportYear", year);
                                request.AddOrUpdateRequestParameter("reportId", id);
                                List<ResponseInfo> responseList = request.GetResponseInfo(_requestXml.GetRequestListByName("reportDetail"));
                                if (responseList != null && responseList.Count > 0)
                                {
                                    LoadAndParseReportDetail(responseList[0].Data, report);
                                }

                                reportList.Add(report);
                            }
                        }
                    }
                }
            }
            _enterpriseInfo.reports = reportList;
        }

        private void LoadAndParseReportNew(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            try
            {
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(responseData);
                HtmlNode rootNode = document.DocumentNode;
                RequestHandler request = new RequestHandler();
                request.ResponseEncoding = "gb2312";

                var trReports = rootNode.SelectNodes("//tr[contains(@id,'tr_nbxx')]");
                if (trReports != null && trReports.Any())
                {
                    foreach (var trReport in trReports)
                    {

                        var tdReports = trReport.ChildNodes.Where(p => p.Name == "td").ToList();
                        var aReport = tdReports[3].ChildNodes.FirstOrDefault(p => p.Name == "a");
                        Report reportInfo = new Report()
                        {
                            name = _enterpriseInfo.name,
                            reg_no = _enterpriseInfo.reg_no,
                            report_date = tdReports[2].InnerText
                        };
                        reportInfo.report_name = tdReports[1].InnerText;
                        reportInfo.report_year = tdReports[1].InnerText.Substring(0, 4);

                        if (reportsNeedToLoad.Count == 0 || reportsNeedToLoad.Contains(reportInfo.report_year))
                        {
                            //获取年报详情
                            if (aReport != null)
                            {
                                var detailParams = string.Format("method={0}&pripid={1}&nd={2}&random={3}", "qyinfo_nnbxx", this._requestInfo.Parameters["pripid"], reportInfo.report_year, DateTime.Now.Ticks);
                                var responseStr = request.HttpGet("http://sn.gsxt.gov.cn/ztxy.do", detailParams);

                                LoadAndParseReportDetailNew(responseStr, reportInfo);

                                _enterpriseInfo.reports.Add(reportInfo);
                            }
                        }
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

            // 企业基本信息
            HtmlNode divs = rootNode.SelectSingleNode("//div[@id='qufenkuang']");
            if (divs != null)
            {
                HtmlNodeCollection tables = divs.SelectNodes("./table");
                if (tables != null)
                {
                    foreach (HtmlNode table in tables)
                    {
                        string header = table.SelectNodes("./tr/th")[0].InnerText.Trim();
                        if (header.EndsWith("红色为修改过的信息项"))
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
                                        switch (thList[i].InnerText.Trim())
                                        {
                                            case "注册号":
                                            case "营业执照注册号":
                                                report.reg_no = tdList[i].InnerText.Trim().Replace("&nbsp;", "");
                                                break;
                                            case "统一社会信用代码":
                                                report.credit_no = tdList[i].InnerText.Trim().Replace("&nbsp;", "");
                                                break;
                                            case "注册号/统一社会信用代码":
                                            case "统一社会信用代码/注册号":
                                                if (tdList[i].InnerText.Trim().Replace("&nbsp;", "").Length == 18)
                                                    report.credit_no = tdList[i].InnerText.Trim().Replace("&nbsp;", "");
                                                else
                                                    report.reg_no = tdList[i].InnerText.Trim().Replace("&nbsp;", "");
                                                break;
                                            case "名称":
                                            case "企业名称":
                                                report.name = tdList[i].InnerText.Trim().Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                                                break;
                                            case "联系电话":
                                            case "企业联系电话":
                                                report.telephone = tdList[i].InnerText.Trim();
                                                break;
                                            case "企业通信地址":
                                                report.address = tdList[i].InnerText.Trim();
                                                break;
                                            case "邮政编码":
                                                report.zip_code = tdList[i].InnerText.Trim();
                                                break;
                                            case "电子邮箱":
                                            case "企业电子邮箱":
                                                report.email = tdList[i].InnerText.Trim();
                                                break;
                                            case "是否有投资信息或购买其他公司股权":
                                            case "是否有对外投资设立企业信息":
                                                report.if_invest = tdList[i].InnerText.Trim();
                                                break;
                                            case "是否有网站或网店":
                                            case "是否有网站或网点":
                                                report.if_website = tdList[i].InnerText.Trim();
                                                break;
                                            case "企业经营状态":
                                                report.status = tdList[i].InnerText.Trim();
                                                break;
                                            case "从业人数":
                                                report.collegues_num = tdList[i].InnerText.Trim();
                                                break;
                                            case "有限责任公司本年度是否发生股东股权转让":
                                                report.if_equity = tdList[i].InnerText.Trim();
                                                break;
                                            case "经营者姓名":
                                                report.oper_name = tdList[i].InnerText.Trim();
                                                break;
                                            case "资金数额":
                                                report.total_equity = tdList[i].InnerText.Trim();
                                                break;
                                            default:
                                                break;
                                        }
                                    }
                                }
                            }
                        }
                        else if (header.EndsWith("企业资产状况信息"))
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
                                        switch (thList[i].InnerText.Trim())
                                        {
                                            case "资产总额":
                                                report.total_equity = tdList[i].InnerText.Trim();
                                                break;
                                            case "负债总额":
                                                report.debit_amount = tdList[i].InnerText.Trim();
                                                break;
                                            case "销售总额":
                                                report.sale_income = tdList[i].InnerText.Trim();
                                                break;
                                            case "其中：主营业务收入":
                                                report.serv_fare_income = tdList[i].InnerText.Trim();
                                                break;
                                            case "利润总额":
                                                report.profit_total = tdList[i].InnerText.Trim();
                                                break;
                                            case "净利润":
                                                report.net_amount = tdList[i].InnerText.Trim();
                                                break;
                                            case "纳税总额":
                                                report.tax_total = tdList[i].InnerText.Trim();
                                                break;
                                            case "所有者权益合计":
                                                report.profit_reta = tdList[i].InnerText.Trim();
                                                break;
                                            default:
                                                break;
                                        }
                                    }
                                }
                            }
                        }

                        // 网站或网店信息
                        List<ResponseInfo> responseList = GetResponseInfo(_requestXml.GetRequestListByGroup("reportBasic"));
                        foreach (var item in responseList)
                        {
                            if (item.Name == "website")
                            {
                                LoadAndParseReportWebsite(item.Data, report);
                            }
                            else if (item.Name == "reportPartner")
                            {
                                LoadAndParseReportPartner(item.Data, report);
                            }
                            else if (item.Name == "invest")
                            {
                                LoadAndParseReportInvest(item.Data, report);
                            }
                        }
                    }
                }
            }
        }
        /// <summary>
        /// 解析报表详情
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="report"></param>
        private void LoadAndParseReportDetailNew(string responseData, Report report)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            var basicTB = rootNode.SelectSingleNode("//table[@class='table_xq']");
            var websitTB = rootNode.SelectNodes("//div[@class='part']/ul[@class='wzwd_list']");//网站
            var gdTB = rootNode.SelectSingleNode("//table[@id='table_gdxx']");//股东及出资信息
            var stockTB = rootNode.SelectSingleNode("//table[@class='table_gqbg']");//股权变更
            var xgxxTB = rootNode.SelectSingleNode("//table[@id='table_xgxx']");//修改信息
            var dwdbTB = rootNode.SelectSingleNode("//table[@id='table_dwdb']");
            if (basicTB != null) LoadAndParseReportBasic(report, basicTB);
            if (websitTB != null) LoadAndParseReportWebsiteNew(report, websitTB);
            if (gdTB != null) LoadAndParseReportPartnerNew(report, gdTB);
            if (stockTB != null) LoadAndParseReportStockChangeNew(report, stockTB);
            if (dwdbTB != null) LoadAndParseReportDWDB(report, dwdbTB);
            if (xgxxTB != null) LoadAndParseReportXGXX(report, xgxxTB);

            var divs = rootNode.SelectNodes("//div[@class='part']");
            if (divs != null)
            {
                foreach (var div in divs)
                {
                    var p = div.SelectSingleNode("./p");
                    if (p != null && p.InnerText.Trim()=="企业资产状况信息")
                    {
                        LoadAndParseReportQyzczkTBNew(report, div.SelectSingleNode("./table"));
                    }
                    else if (p != null && p.InnerText.Trim() == "社保信息")
                    {
                        this.LoadAndParseReportSheBao(report, div);
                    }
                }
            }
        }
        /// <summary>
        /// 加载解析企业基本信息
        /// </summary>
        /// <param name="report"></param>
        /// <param name="table"></param>
        private void LoadAndParseReportBasic(Report report,HtmlNode table) 
        {
            var tds = table.SelectNodes("./tr/td");
            for (var i = 0; i < tds.Count; i++)
            {
                var title = tds[i].InnerText.Split('：', ':')[0].Replace("&nbsp;", "").Replace("·", "").Trim();
                switch (title)
                {
                    case "注册号":
                    case "营业执照注册号":
                        report.reg_no = tds[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                        break;
                    case "统一社会信用代码":
                        report.credit_no = tds[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                        break;
                    case "注册号/统一社会信用代码":
                    case "统一社会信用代码/注册号":
                        if ( tds[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "").Length == 18)
                            report.credit_no = tds[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                        else
                            report.reg_no = tds[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                        break;
                    case "名称":
                    case "企业名称":
                        report.name =  tds[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "").Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                        break;
                    case "联系电话":
                    case "企业联系电话":
                        report.telephone = tds[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                        break;
                    case "企业通信地址":
                        report.address = tds[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                        break;
                    case "邮政编码":
                        report.zip_code = tds[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                        break;
                    case "电子邮箱":
                        report.email = tds[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                        break;
                    case "企业是否有投资信息或购买其他公司股权":
                    case "企业是否有对外投资设立企业信息":
                        report.if_invest = tds[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                        break;
                    case "是否有网站或网店":
                    case "是否有网站或网点":
                        report.if_website = tds[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                        break;
                    case "企业经营状态":
                        report.status = tds[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                        break;
                    case "从业人数":
                        report.collegues_num = tds[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                        break;
                    case "有限责任公司本年度是否发生股东股权转让":
                        report.if_equity = tds[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                        break;
                    case "经营者姓名":
                        report.oper_name = tds[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                        break;
                    case "资金数额":
                        report.total_equity = tds[i].InnerText.Split('：', ':')[1].Trim().Replace("&nbsp;", "");
                        break;
                    default:
                        break;
                
                }
            }
            
        }
        /// <summary>
        /// 加载解析网站
        /// </summary>
        /// <param name="report"></param>
        /// <param name="table"></param>
        private void LoadAndParseReportWebsiteNew(Report report, HtmlNodeCollection tables)
        {
            foreach (var table in tables)
            {
                if (table.SelectSingleNode("./preceding-sibling::p[1]").InnerText.Contains("网站或网店信息"))
                {
                    var trs = table.SelectNodes("./li");
                    if (trs == null) { return; }
                    foreach (var tr in trs)
                    {
                        var span1 = tr.SelectSingleNode("./p[@class='span1']");
                        var span2 = tr.SelectNodes("./p[@class='span2']");
                        var website = new WebsiteItem();
                        if (span2 != null && span2.Count > 0)
                        {
                            website.web_type = span2[0].InnerText.Replace("\r", "").Replace("\n", "").Trim().Replace("类型：", "").Replace("&bull;", "");
                        }
                        if (span2 != null && span2.Count > 1)
                        {
                            website.web_url = span2[1].Attributes["title"].Value.Replace("\r", "").Replace("\n", "").Trim();
                        }
                        if (span1 != null)
                        {
                            website.web_name = span1.Attributes["title"].Value.Replace("\r", "").Replace("\n", "").Trim();
                        }

                        report.websites.Add(website);
                    }
                }
                else if (table.SelectSingleNode("./preceding-sibling::p[1]").InnerText.Contains("对外投资信息"))
                {
                    var trs = table.SelectNodes("./li");
                    if (trs == null) { return; }
                    foreach (var tr in trs)
                    {
                        var span1 = tr.SelectSingleNode("./p[@class='span1']");
                        var span2 = tr.SelectSingleNode("./p[@class='span2']");
                        var tds = tr.ChildNodes.Where(p => p.Name == "td").ToList();
                        var invest = new InvestItem();
                        invest.invest_name = span1.Attributes["title"].Value.Replace("\r", "").Replace("\n", "").Trim();
                        invest.invest_reg_no = span2.Attributes["title"].Value.Replace("\r", "").Replace("\n", "").Trim();
                        report.invest_items.Add(invest);
                    }
                }
            }
        }
        /// <summary>
        /// 加载解析年报股东
        /// </summary>
        /// <param name="report"></param>
        /// <param name="table"></param>
        private void LoadAndParseReportPartnerNew(Report report, HtmlNode table)
        {
            var trs = table.SelectNodes("./tr[@name='gdxx']");
            if (trs == null) { return; }
            foreach (var tr in trs)
            {
                var tds = tr.ChildNodes.Where(p => p.Name == "td").ToList();
                var partner = new Partner()
                {
                    seq_no = report.partners.Count + 1,
                    stock_name = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Trim()
                };
                var rci = new RealCapiItem()
                {
                    real_capi = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Replace("&nbsp;", "").Trim(),
                    real_capi_date = tds[6].InnerText.Replace("\r", "").Replace("\n", "").Trim(),
                    invest_type = tds[7].InnerText.Replace("\r", "").Replace("\n", "").Trim()

                };
                var sci = new ShouldCapiItem()
                {
                    shoud_capi = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Replace("&nbsp;", "").Trim(),
                    should_capi_date = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Trim(),
                    invest_type = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Trim()


                };
                partner.real_capi_items.Add(rci);
                partner.should_capi_items.Add(sci);
                report.partners.Add(partner);
            }
        }

        /// <summary>
        /// 加载解析年报股权变更
        /// </summary>
        /// <param name="report"></param>
        /// <param name="table"></param>
        private void LoadAndParseReportStockChangeNew(Report report, HtmlNode table)
        {
            var trs = table.SelectNodes("./tr");
            if (trs == null) { return; }
            foreach (var tr in trs)
            {
                var tds = tr.ChildNodes.Where(p => p.Name == "td").ToList();
                if (tds.Count > 4)
                {
                    var stock = new StockChangeItem()
                    {
                        name = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Trim(),
                        before_percent = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Trim(),
                        after_percent = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Trim(),
                        change_date = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Trim(),
                       public_date = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Trim()
                    };
                    report.stock_changes.Add(stock);
                }
            }
        }


        /// <summary>
        /// 加载解析年报股权变更
        /// </summary>
        /// <param name="report"></param>
        /// <param name="table"></param>
        private void LoadAndParseReportDWDB(Report report, HtmlNode table)
        {
            var trs = table.SelectNodes("./tr[@name='dwdb']");
            if (trs == null) { return; }
            foreach (var tr in trs)
            {
                var tds = tr.ChildNodes.Where(p => p.Name == "td").ToList();
                var ext = new ExternalGuarantee()
                {
                    seq_no = report.external_guarantees.Count+1,
                    creditor = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Trim(),
                    debtor = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Trim(),
                    guarantee_type = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Trim(),
                    amount = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Trim(),
                    period = tds[5].InnerText.Replace("\r", "").Replace("\n", "").Trim(),
                    guarantee_time = tds[6].InnerText.Replace("\r", "").Replace("\n", "").Trim(),
                    type = tds[7].InnerText.Replace("\r", "").Replace("\n", "").Trim()
                };

                report.external_guarantees.Add(ext);
            }
        }

        /// <summary>
        /// 加载解析年报股权变更
        /// </summary>
        /// <param name="report"></param>
        /// <param name="table"></param>
        private void LoadAndParseReportXGXX(Report report, HtmlNode table)
        {
            var trs = table.SelectNodes("./tr[@name='xgxx']");
            if (trs == null) { return; }
            foreach (var tr in trs)
            {
                var tds = tr.ChildNodes.Where(p => p.Name == "td").ToList();
                var ext = new UpdateRecord()
                {
                     seq_no = report.update_records.Count+1,
                    update_item = tds[1].InnerText.Replace("\r", "").Replace("\n", "").Trim(),
                     before_update = tds[2].InnerText.Replace("\r", "").Replace("\n", "").Trim(),
                      after_update = tds[3].InnerText.Replace("\r", "").Replace("\n", "").Trim(),
                      update_date = tds[4].InnerText.Replace("\r", "").Replace("\n", "").Trim()
                };
                report.update_records.Add(ext);
            }
        }


        private void LoadAndParseReportQyzczkTBNew(Report report, HtmlNode table)
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
                        switch (thList[i].InnerText.Trim())
                        {
                            case "资产总额":
                                report.total_equity = tdList[i].InnerText.Trim();
                                break;
                            case "负债总额":
                                report.debit_amount = tdList[i].InnerText.Trim();
                                break;
                            case "销售总额":
                            case "营业总收入":
                            case "销售额或营业收入":
                                report.sale_income = tdList[i].InnerText.Trim();
                                break;
                            case "主营业务收入":
                            case "其中：主营业务收入":
                            case "营业总收入中主营业务收入":
                                report.serv_fare_income = tdList[i].InnerText.Trim();
                                break;
                            case "利润总额":
                                report.profit_total = tdList[i].InnerText.Trim();
                                break;
                            case "净利润":
                                report.net_amount = tdList[i].InnerText.Trim();
                                break;
                            case "纳税总额":
                                report.tax_total = tdList[i].InnerText.Trim();
                                break;
                            case "所有者权益合计":
                                report.profit_reta = tdList[i].InnerText.Trim();
                                break;
                            default:
                                break;
                        }
                    }
                }
            }
        }

        void LoadAndParseReportSheBao(Report report, HtmlNode div)
        {
            HtmlNodeCollection trList = div.SelectNodes("./table/tr");

            foreach (HtmlNode rowNode in trList)
            {
                HtmlNodeCollection thList = rowNode.SelectNodes("./th");
                HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                if (tdList == null)
                {
                    thList.Add(thList.First().SelectSingleNode("./th"));
                    tdList = thList.First().SelectNodes("./td");
                    tdList.Add(thList.Last().SelectSingleNode("./td"));
                }

                if (thList != null && tdList != null)
                {
                    if (thList.Count > tdList.Count)
                    {
                        thList.Remove(0);
                    }
                    for (int i = 0; i < thList.Count; i++)
                    {
                        if (thList[i].SelectSingleNode("./font") == null) continue;
                        switch (thList[i].SelectSingleNode("./font").InnerText.Trim())
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
        private void LoadAndParseReportWebsite(string responseData, Report report)
        {
            List<WebsiteItem> websiteList = new List<WebsiteItem>();
            WebsiteSHANXI websiteHN = JsonConvert.DeserializeObject<WebsiteSHANXI>(responseData);
            int i = 1;
            foreach (WebsiteJsonSHANXI websiteJson in websiteHN.list)
            {
                WebsiteItem item = new WebsiteItem();

                item.seq_no = i++;
                item.web_type = websiteJson.wzlxLabel;
                item.web_name = websiteJson.wzmc;
                item.web_url = websiteJson.wzdz;

                websiteList.Add(item);
            }
            report.websites = websiteList;
        }


        private void LoadAndParseReportPartner(string responseData, Report report)
        {
            List<Partner> partnerList = new List<Partner>();
            ReportPartnerSHANXI partnerHN = JsonConvert.DeserializeObject<ReportPartnerSHANXI>(responseData);
            int i = 1;
            foreach (ReportPartnerJsonSHANXI websiteJson in partnerHN.list)
            {
                Partner item = new Partner();

                item.seq_no = i++;
                item.stock_name = websiteJson.name;
                item.stock_type = websiteJson.czfsLabel;
                item.identify_no = "";
                item.identify_type = "";
                item.stock_percent = "";
                item.ex_id = "";
                item.should_capi_items = new List<ShouldCapiItem>();
                item.real_capi_items = new List<RealCapiItem>();

                ShouldCapiItem sItem = new ShouldCapiItem();
                var sCapi = websiteJson.yjcze;
                sItem.shoud_capi = string.IsNullOrEmpty(sCapi) ? "" : sCapi;
                sItem.should_capi_date = "";
                sItem.invest_type = websiteJson.czfsLabel;
                item.should_capi_items.Add(sItem);

                RealCapiItem rItem = new RealCapiItem();
                var rCapi = websiteJson.sjcze;
                rItem.real_capi = string.IsNullOrEmpty(rCapi) ? "" : rCapi;
                rItem.real_capi_date = "";
                rItem.invest_type = websiteJson.czfsLabel;
                item.real_capi_items.Add(rItem);
                partnerList.Add(item);
            }
            report.partners = partnerList;
        }


        private void LoadAndParseReportInvest(string responseData, Report report)
        {
            //        List<InvestItem> investList = new List<InvestItem>();
            //        j = 1;
            //        foreach (HtmlNode rowNode in trList)
            //        {
            //            HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
            //            if (tdList != null && tdList.Count > 1)
            //            {
            //                InvestItem item = new InvestItem();

            //                item.seq_no = j++;
            //                item.invest_name = tdList[0].InnerText.Trim();
            //                item.invest_reg_no = tdList[1].InnerText.Trim();

            //                investList.Add(item);
            //            }
            //        }
            //        report.invest_items = investList;
        }

        DataRequest CreateRequest(string encoding = "gb2312")
        {
            Dictionary<string, string> dic = new Dictionary<string, string>();
            RequestInfo rInfo = new RequestInfo()
            {
                Cookies = _requestInfo.Cookies,
                Headers = _requestInfo.Headers,
                Province = _requestInfo.Province,
                CurrentPath = _requestInfo.CurrentPath,
                Referer = _requestInfo.Referer,
                ResponseEncoding = encoding,
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
        void CheckMessageIsError(HtmlNode rootNode)
        {
            var h2 = rootNode.SelectSingleNode("//h2");
            if (h2 != null)
            {
                if (!h2.InnerText.Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace("&nbsp;", "").Replace(" ", "").Contains(_enterpriseName))
                {
                    throw new Exception("陕西网站内容错乱");
                }
            }
        }
    }
}