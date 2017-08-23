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
using MongoDB.Bson;

namespace iOubo.iSpider.Infrastructure.Parser
{
    public class ConverterTJ : IConverter
    {
        DataRequest _request;
        RequestInfo _requestInfo;
        RequestXml _requestXml;
        EnterpriseInfo _enterpriseInfo = new EnterpriseInfo();
        List<AbnormalInfo> _abnormals = new List<AbnormalInfo>();
        List<CheckupInfo> _checkups = new List<CheckupInfo>();
        List<string> reportsNeedToLoad = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings.Get("ReportsNeedToLoad"))
            ? new List<string>() : ConfigurationManager.AppSettings.Get("ReportsNeedToLoad").Split(',').ToList();
        string name = string.Empty;
        Dictionary<string, string> _urls = new Dictionary<string, string>();
        string _entType = string.Empty;
        BsonDocument document = new BsonDocument();
        Dictionary<string, string> _employeePosition = new Dictionary<string, string>();
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
            responseList = _request.GetResponseInfo(_requestXml.GetRequestListByName("gongshang"));
            if (responseList != null && responseList.Any() && !string.IsNullOrWhiteSpace(responseList.First().Data))
            {
                this.LoadAndParseUrls(responseList.First().Data);
                this.LoadAndParseBasic(responseList.First().Data);
            }
            responseList = _request.GetResponseInfo(this.GetRequestSettings());

            ParseResponseMainInfo(responseList);

            SummaryEntity summaryEntity = new SummaryEntity();
            summaryEntity.Enterprise = _enterpriseInfo;
            summaryEntity.Abnormals = _abnormals;
            summaryEntity.Checkups = _checkups;

            return summaryEntity;
        }

        private void InitialEnterpriseInfo()
        {
            this.InitEmployeesPositions();
            _enterpriseInfo.province = this._requestInfo.Province;
            _enterpriseInfo.last_update_time = DateTime.Now;
            _enterpriseInfo.source = "Batch";
            if (this._requestInfo.Parameters.ContainsKey("platform"))
            {
                this._requestInfo.Parameters.Remove("platform");
            }
            _enterpriseInfo.parameters = this._requestInfo.Parameters;
        }

        #region initEmployee
        void InitEmployeesPositions()
        {
            _employeePosition.Add("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAB0AAAAOCAYAAADT0Rc6AAABaUlEQVR42mNgwA7SgNgUiV8OxEoM\nxAN7II4lRmERENtC2cuBOBzKlgbiL0DMg0VPIhDPxoIPAvE1HHJpyAYsRLII2dIuIH4MFUPGWUCs\nB8ReWPAEIN6CQ86AkKXyQPwBiP2waNZD0rsJagkMXwLi+2hi27AFLzZLdwNxLdTyHUAsiyNqPqHx\nk4F4CprYD2IsrYS6jgkqVgDE74DYEYelhHyK1VKQywKwxCky8IHGLxeSmCU0pQYg4SlQByOLgdSI\nY7OYDYpBhmsh8ZExH5qeLKglyHgvEF9BE7sLTQsoAJQl/gPxayjGx4ZlH0do/KHjhVCLkcXOQVO1\nG7qln3BE/A+0+ONBCtpwLHgmNOEhi50E4g6ksoBsSxmgrl+Iho8C8Q00sYe4gpccS62B2AUN90Hz\nLrLYNiiNUgSuAeJfSCXOHxzsX1C1RWiO5oOm0HCoT3MIlbvGOIosfNgYzQxQfl4MdVgPjrIaDAC5\nfoW6ShoocQAAAABJRU5ErkJggg==", "监事");
            _employeePosition.Add("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAB0AAAAOCAYAAADT0Rc6AAABoUlEQVR42r2UT0REURTGr5G0eCJJ\nWkWrFrNLxshom1m0mM1IRkak1WgRLUbSLmmRJDIyRtImaTFGJC3Sov0s0ma0GEkkSVrE9J18L2fu\nvDtv2nT5effdd+89/77zjGkefWDV/PNYA0ecz4JPBxPcMwkyCjkzB94c1E1AlE9gOMSpG/W+AHbp\nyD7YBvNck5EEp5x303DT2ALr/CgX9Frfl8E9GAhw5hl4nIvRPd4zDc4492yjMV7YA/LgUn2TzUXQ\nACuODNhGa6AMbsEj5xXbaJEpksVXpjjC9EkdCmCcB6tgKsRoR+mNEIlwEUTpodQvYRlI0REpxQ4v\navBZo9EyDfoaSDLVLTXNK6/8lHsOhogr0jtQYhAPnB/aRsWLL7bLCTjmWp28gA/1vtkmvV3Uhp1e\no/b89tsByHJjv3Vpho6YDoSUowaEc6a8oBgNukBqG+fzL0Zn2CqigTTZoILTikH/UJx9KjV4p0o7\niVR695qluaIu9LDT26LIJTCmRHShqFLNei1H0aRUDbMUTSlASD6uXv/5JSZCGAk4F2V07Yh9A2Qg\ng+AVrBiLAAAAAElFTkSuQmCC", "经理");
            _employeePosition.Add("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAADkAAAAOCAYAAACVZ7SQAAACqElEQVR42r2WUYSUURTHx1irh8RK\nkqzoIVlj7dsaayWykmRfMg9ZSezDyDysXpL00EvWWvMwljWStTIkSdaItVZW1rL2IUkiWaOHLMk+\njQx1Tn43Z447830zDx1+Zu65937f/d97zrlfJtNui8IR0x4Qqpl0dkWYyPRnt4Qx0x7Dl9YuCDNp\nBx8IR017UGilmHdC+C4UaK90QTeuyOYFPgvrpr2Oz44pIrwa4a3wsUPfbBqRzQSBOn5HWDa+Qhey\nwqhwyVAXHpj2PeEl/y8LU8wZJWI8ZWGtQ9+/CFlFzG9+n4P1NU1fsOPCO+EV7VM9hNh9xL0RvkKD\njd5k0Ws824fua9OvvGe+9dXThOsAIg45UeU6OxzsEaJ17G1hN6XAEXeSgccIiPWNmPmH7nn67orz\nNdPmZE74YdrTTuSQOcEDCk+JF3aiRJGI9WkufujQN+NEJp1kV5G6Y+Psqi5o24msuTlZwusZ7ZjI\nFmEXRIZcrjm2KTjenzPvyyN42lAhPK1Px5z0JfgJ6rVK3mUx2t5KEFkjbytdwlOfM+l8mgobJhUG\nyb0l56sTrsGKkU3ciETAFwpPWxF4SBjYcF0yQocjIheEPfKyH5ENV+43I1fBvhF5kfzzrCDU+vao\nulNJOamhcxUBWuKvkZNZ7sY8i63+J5H5DtfSMlXa+nZIucluIlXQNwTZrxoNn/MsMFg/IvU9c/wG\nZlmw9WnanDPzypEPDL3GPjnfvg9XL3KIQSXXr+J+cpW86EFkK/LJN05UWHbJJe+3cyci18ti5Orx\nufx3l28IvwjDLUInZg1OuJQgUqvtU17WoogEO+0qYaAcqZKBYff8Y1TQAid5J+lyvskRz9GeN3eg\ntxwCB4yvRNWzNo/wMrlk7QwL7IWzkatrlUK44GpJm/0B+H4BqS3ysBwAAAAASUVORK5CYII=", "执行董事");
            _employeePosition.Add("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAB0AAAAOCAYAAADT0Rc6AAABdklEQVR42qWUMUgDQRBFDwsrsbGw\nEBEsLIJIOpEgIkgQK7tUV4iQIoiFnYi9hcgVQZBUYmEnInIIIQQLEUFSiIgIIiGlnVVKZ+EffL4T\nTnHhkezfnZ3d2b8XRX7bMIrUL0L7bVsy4rxJNaNBvBlN6jeh8ZwaNtJwuDVeBoxVs6RzxgqRGvvU\n3zUu8H/VKCMmsOaQGNcDxriC0R6S3RgfoGd8Gm0sErh0Sn1F44EnxLOWankLctKMAyzojRUo/kvW\n2zTqovU1aYxJSrjL5wFjsSTNO2nfM9OIcS7cw0Cqz1LcAjawTtRRTtbCnHFNOma0jGEi3N2xaCnK\ny87XKrScCr3DSD+S9sTebcf6XUq6jPtTTpGYtQ5cXf5v0lDaisMJXgFrDzDmot7pDn4zqliAtW1j\nhuISnIy5M15F63rlnccHgHnEXaheoriS85yOnKemXogmxGkZiePCjEnZ9CgcWsFJt/K+vVMI+AvT\nssaQcYYndYircNs3bFSWiVYudnIAAAAASUVORK5CYII=", "董事");
            _employeePosition.Add("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACsAAAAOCAYAAAC2POVFAAAB/0lEQVR42rWVQUSDYRjHJ5MOiUw6\nTCJJJtltMpmYyQ7pNh12SOwwSbol6dClQ7LDRHaaDpEkySdmpkMS0yFJIpkOHbqlw+xSz5v/x9Pj\n+fZ+O/Tys33P+z7f+7zP+3+eLxDQxyIRZc9R2PyOBJH1sS5CFC1rgtKQJ0qMZ6LCniuw8TV5HKCk\ncEU8eszl2L5J4sMjyH6iTBzLiUk4ujjEJnteJ07xf5ZIwceQVigQFx5zUR/BLhHvxAkxoJ1kA0Fe\nEq/gDS+rYXPDmSKJczZvuIc/tznKnjzYbtyWucEnHMxTO0mFHQSizUWY/6eSGanFZptg+4gvok4s\n2ISexcslRqsPHnNZEawts01LZoc6KOBAL3EkuMG1SPsE85tC4POMIq6d28yaQfiYwvnukD83FSKq\n0I6L0ea+sDnICO8kMutV5UZeFB3KAssiQdYRQkHxNlNTWlCDBTsDfUrKCJjb7tAlUm2C7cJ+uf8I\n1kggo3CArsJttyjYaUvrikHfSZtm1/DrksPG3LZCjDG/AjLJuUb74baGDxmME2EcrgWJqSOGxs+p\nQ2vSHmd+caWt7Sktz1Gy5QabwHyLfTTSmDPymeNOYVG5LgWlql1kq+lDgWSQ2WUfdbKKSjdBbSlf\nqxCKswUJ/Y5hbNQJI+LFpjgO0dp2IRnbGCW2iR7LOtPygj81EMiFCWOxNQAAAABJRU5ErkJggg==", "董事长");
            _employeePosition.Add("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACsAAAAOCAYAAAC2POVFAAACPUlEQVR42sWVX2SVYRzHHzNHcsSR\nJF2MyWR2l5kk3SXnYjKOYzKZMV0dk9jFJF3EZCZJzMwcSczMLmZipovM7H4XGZkuJhlJki5ifX98\n3vl59j7vToz9+DjP8573eZ7fn+/ze0M4alfEg5znDf7Ls4p4HE7BLog9Meie3RTfxMXEmifiLeN7\n4k+CG7xzSww5bM198TPBXt6hVZgUz9z8s5h28zirFkhHQQIsmA03HxWvCGBGvBAjPMv8WGJcwuEj\ndiAWxLsEC7zjbUo8ZVM7+Fz0/yOxQ8Vi2xdlxubsa/bpF8uMy0XOlgoyVIqc7cORM2JCrLv/7JB5\n3h9P7Bc7uytWxJb4ynj1pJydp5S22Q+k0EaZTWezopcDt8WdY5z9bxnUxN0EtcjZNling/SQkQ0u\npbcBAjDJvMSBA353cXYFRzONV5HEiWl2wmUhk0Y5wSVIZfaTaBL8F8ZvUs5WyVSW/k3RHWWy5uYW\n9V/a1iIB9ZNB47v47ebPC2TQjvZjGQT3zqHNobUq5Qj0wB3aU6DP2qGdrl/aumHWnY/2HCKA0MIF\na6Bx4z3SmHVczRadJQMdkbMBjdnz27ShUfpuJTrYsn7dVaZVZwdpWabxOkzSEeqOww/SQ6LJSrCW\ns/E4mwbKPo1zU2jsF7e+lcxa0B+R0Ad0H8txKRVhnYPNLpPlpivBHF+pXqehdm74mLjmLteaY5vu\n4J81uEwDTqPDnNfMuWAZqV4duqIS1GlLx1mFchbRmbOux33KU/T9A1cGuQIA/VG9AAAAAElFTkSu\nQmCC", "总经理");
            _employeePosition.Add("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAADkAAAAOCAYAAACVZ7SQAAACk0lEQVR42rWWQWRcURSGn4roYoSI\n6qKqVESMitlVRUSJqCxidhExiwgjYlR1U1XVRZUsqkY9pSIisggREREjjIgsIsLoIqqqVEXULGYX\nXTxPaM+p/9bpce6dN6WXz9x37rtzz3/vOee+KOq8dREznrFZoiCeC7BlbaNEKcN7eSLO4OdfbYW4\nCPBWvJsjEvQXiCXBF6IunuuwyXcWIHzJ4JD45BkrCx/GiJZHXC+xSmz8wwFGW0RRiRzCgo4a8Vw8\nP8U87j8gxjGHmTCoEruesUIGkXNEk9gkrlkiFvHCd0ETdp9Ibs8gbo/4Bs7hxAGcZraN0N0R48wp\n5ktbzfBViuxGdHDEfMaGeFuMndA7EwdE5tVJOhYhwBrLi/+/CKznWhIQ2UP8IBrEdJZwjJETOkdC\nIksY13AufvSMlZTIdieZtDnJm53kHC9eUXlQEU75wpWf1xXHCB9tvyPm3cN/FwUxwlPa+J3rmMMF\n5WeH/ImMq/gDH7mAyD5iH7nh4Nx7p2w1nEAkKrM+5X0jAr4aeaYLTwkbG2w5hI7Lm1T0GxASEnmu\nwvzAuArOhMj7yD/NKoRK2wdU3fGAyCtYr5xF5AhIRf/kP4jkUJ0yeI8qLW0nKGQjba6Qu/BrLCQy\nEblzKfpNCBnC3WPl5GP8OspwWNoeEgNiXtVIiyNcA9J2liFcB4kb2JQUqWCKbHnKdh0i5RdFonZw\nS9FALmn7sJg3bFwvb4yrp2acjhM5ivFUfCxMYIzDfNI6SVe2L0W/BZFPUAj2ICDC7hUNqkaVdOiS\n34PCMYWTrGS4CR6hcrJvL4yvmz74moqPmd/VdV1VQ8crbMI8sYz8crt2Cw52wm3lEBeNNaz/Gmu1\na/3ES/gdanz1dP0CG38SpdFXf2kAAAAASUVORK5CYII=", "副董事长");

        }
        #endregion
        #region 解析工商公示信息
        /// <summary>
        /// 解析工商公示信息
        /// </summary>
        /// <param name="responseInfoList"></param>
        private void ParseResponseMainInfo(List<ResponseInfo> responseInfoList)
        {
            //Parallel.ForEach(responseInfoList, new ParallelOptions { MaxDegreeOfParallelism = 10 }, responseInfo => this.LoadData(responseInfo));
            foreach(var responseInfo in responseInfoList)
                this.LoadData(responseInfo);
        }
        #endregion

        private void LoadData(ResponseInfo responseInfo)
        {
            switch (responseInfo.Name)
            {
                case "partner":
                    this.LoadAndParsePartner(responseInfo.Data);
                    break;
                case "employee":
                    this.LoadAndParseEmployee(responseInfo.Data);
                    break;
                case "branch":
                    this.LoadAndParseBranch(responseInfo.Data);
                    break;
                case "changerecord":
                    this.LoadAndParseChangeRecord(responseInfo.Data);
                    break;
                case "stakQualit":
                    this.LoadAndParseStakQualit(responseInfo.Data);
                    break;                
                case "report":
                    this.LoadAndParseReports(responseInfo.Data);
                    break;
                case "abnormal":
                    this.LoadAndParseAbnormal(responseInfo.Data);
                    break;
                case"freeze":
                    this.LoadAndParseFreezes(responseInfo.Data);
                    break;
                default:
                    break;
            }
        }

        #region 解析司法协助
        void LoadAndParseFreezes(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            this.LoadAndParseFreezesContent(responseData);
            var totalPage = document.Contains("totalPage") ? (document["totalPage"].IsBsonNull ? 1 : document["totalPage"].AsInt32) : 1;
            if (totalPage > 1)
            {
                this.LoadAndParseFreezesByPage(totalPage);
            }
        }
        #endregion

        #region 解析司法协助分页
        void LoadAndParseFreezesByPage(int totalPage)
        {
            for (int i = 2; i <= totalPage; i++)
            {
                List<RequestSetting> list = new List<RequestSetting>();
                list.Add(new RequestSetting
                {
                    Method = "post",
                    Url = string.Format("{0}{1}", "http://tj.gsxt.gov.cn", _urls["assistUrl"]),
                    IsArray = "0",
                    Name = "freeze",
                    Data = string.Format("draw={0}&start={1}&length=5", i.ToString(), ((i - 1) * 5).ToString())
                });
                var responseList = _request.GetResponseInfo(list);
                if (responseList != null && responseList.Any())
                {
                    this.LoadAndParseFreezesContent(responseList.First().Data);
                }
            }
        }
        #endregion

        #region 司法协助内容
        private void LoadAndParseFreezesContent(string responseData)
        {

            QGSfxzList list = JsonConvert.DeserializeObject<QGSfxzList>(responseData);           

            if (list != null && list.data != null && list.data.Length>0)
            {
                foreach (QGSfxzInfo item in list.data)
                {
                    Utility.ClearNullValue<QGSfxzInfo>(item);
                    JudicialFreeze freeze = new JudicialFreeze();
                    freeze.seq_no = _enterpriseInfo.judicial_freezes.Count + 1;
                    freeze.be_executed_person = item.inv;
                    freeze.amount = string.IsNullOrEmpty(item.froAm) ? "" : item.froAm+"万"+item.regCapCur_CN;
                    freeze.executive_court = item.froAuth;
                    freeze.number = string.IsNullOrEmpty(item.executeNo) ? string.Empty : item.executeNo;
                    if (item.type == "1" && item.frozState == "1") 
                        freeze.status = "股权冻结|冻结";
                    else                    
                        freeze.status = item.frozState == "2" ? "股权冻结|解除冻结" : item.frozState == "3"?"股权冻结|失效":"股权变更";
                    if (item.type == "1")
                    {
                        List<RequestSetting> req = new List<RequestSetting>();
                        req.Add(new RequestSetting
                        {
                            Method = "get",
                            Url = string.Format("http://tj.gsxt.gov.cn/corp-query-entprise-info-judiciaryStockfreeze-{0}.html", item.parent_Id),
                            IsArray = "0",
                            Name = "freeze_detail"
                        });
                        var responseList = _request.GetResponseInfo(req);
                        if (responseList != null && responseList.Any())
                        {
                            this.LoadAndParseFreezesDetail(responseList.First().Data, freeze);
                        }
                    }                    
                
                    _enterpriseInfo.judicial_freezes.Add(freeze);
                }
            }            
        }
        #endregion

        #region 股权冻结详细内容
       void  LoadAndParseFreezesDetail(string response ,JudicialFreeze freeze)
        {
            QGSfxzList free = JsonConvert.DeserializeObject<QGSfxzList>(response);              
            JudicialFreezeDetail detail = new JudicialFreezeDetail();
            JudicialUnFreezeDetail un_freeze_detail = new JudicialUnFreezeDetail();
            if (free.data != null && free.data.Length > 0)
            {
                for (int i = 0; i < free.data.Length; i++)
                {
                    QGSfxzInfo gqdj = free.data[i];
                    if (gqdj.frozState == "1" && gqdj.keepFrozDeadline != "")
                    {
                        Utility.ClearNullValue<QGSfxzInfo>(gqdj);
                        detail.adjudicate_no = gqdj.froDocNo;
                        detail.execute_court = gqdj.froAuth;
                        detail.assist_name = gqdj.inv;
                        detail.freeze_amount = string.IsNullOrEmpty(gqdj.froAm) ? "" : gqdj.froAm + "万" + gqdj.regCapCur_CN;
                        detail.assist_item = gqdj.executeItem_CN;
                        if (gqdj.cerType_CN != "")
                            detail.assist_ident_type = gqdj.cerType_CN; 
                        else if (gqdj.cerType_CN == "" && gqdj.bLicType_CN != "")
                        {
                            detail.assist_ident_type = gqdj.bLicType_CN;
                            detail.assist_ident_no = gqdj.bLicNo;
                        }
                        detail.freeze_start_date = string.IsNullOrEmpty(gqdj.keepFroFrom) ? "" : ConvertStringToDate(gqdj.keepFroFrom);
                        detail.freeze_end_date = string.IsNullOrEmpty(gqdj.keepFroTo) ? "" : ConvertStringToDate(gqdj.keepFroTo);
                        detail.freeze_year_month = gqdj.keepFrozDeadline;
                        detail.freeze_amount = gqdj.froAm;
                        detail.notice_no = string.IsNullOrEmpty(gqdj.executeNo) ? string.Empty : gqdj.executeNo;
                        detail.public_date = string.IsNullOrEmpty(gqdj.publicDate) ? "" : ConvertStringToDate(gqdj.publicDate);
                        freeze.detail = detail;
                    }
                    if (gqdj.frozState == "2")
                    {
                        Utility.ClearNullValue<QGSfxzInfo>(gqdj);
                        un_freeze_detail.adjudicate_no = gqdj.froDocNo;
                        un_freeze_detail.execute_court = gqdj.froAuth;
                        un_freeze_detail.assist_name = gqdj.inv;
                        un_freeze_detail.freeze_amount = string.IsNullOrEmpty(gqdj.froAm) ? "" : gqdj.froAm + "万" + gqdj.regCapCur_CN;
                        un_freeze_detail.assist_item = gqdj.executeItem_CN;
                        if (gqdj.cerType_CN != "")
                            un_freeze_detail.assist_ident_type = gqdj.cerType_CN;
                        else if (gqdj.cerType_CN == "" && gqdj.bLicType_CN != "")
                        {
                            un_freeze_detail.assist_ident_type = gqdj.bLicType_CN;
                            un_freeze_detail.assist_ident_no = gqdj.bLicNo;
                        }
                        un_freeze_detail.unfreeze_date = string.IsNullOrEmpty(gqdj.thawDate) ? "" : ConvertStringToDate(gqdj.thawDate) ;
                        un_freeze_detail.notice_no = string.IsNullOrEmpty(gqdj.executeNo) ? string.Empty : gqdj.executeNo;
                        un_freeze_detail.public_date = string.IsNullOrEmpty(gqdj.publicDate) ? "" : ConvertStringToDate(gqdj.publicDate);
                        freeze.un_freeze_detail = un_freeze_detail;
                    }
                }
            }
        }
        #endregion
       #region 解析经营异常
       void LoadAndParseAbnormal(string responseData)
        {            
            BsonDocument document = BsonDocument.Parse(responseData);
            this.LoadAndParseAbnormalContent(responseData);
            var totalPage = document.Contains("totalPage") ? (document["totalPage"].IsBsonNull ? 1 : document["totalPage"].AsInt32) : 1;
            if (totalPage > 1)
            {
                this.LoadAndParseAbnormalByPage(totalPage);
            }
        }
        #endregion
        

        #region 解析经营异常分页
        void LoadAndParseAbnormalByPage(int totalPage)
        {
            for (int i = 2; i <= totalPage; i++)
            {
                List<RequestSetting> list = new List<RequestSetting>();
                list.Add(new RequestSetting
                {
                    Method = "post",
                    Url = string.Format("{0}{1}", "http://tj.gsxt.gov.cn", _urls["indBusExcepUrl"]),
                    IsArray = "0",
                    Name = "abnormal",
                    Data = string.Format("draw={0}&start={1}&length=5", i.ToString(), ((i - 1) * 5).ToString())
                });
                var responseList = _request.GetResponseInfo(list);
                if (responseList != null && responseList.Any())
                {
                    this.LoadAndParseAbnormalContent(responseList.First().Data);
                }
            }
        }
        #endregion

        #region 解析经营异常内容
        void LoadAndParseAbnormalContent(string response)
        {
            QGJyycList cqInfo = JsonConvert.DeserializeObject<QGJyycList>(response);
            
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
                  //  list.Add(dItem);
                    this._abnormals.Add(dItem);
                }
            }
            
        }
        #endregion

        #region 解析reportlist
        private void LoadAndParseReports(string response)
        {            
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
                    if ((item.annRepFrom!=null&&item.annRepFrom !="2") || (item.entType!=null && item.entType!="16"))                
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
                    QGReportAlterList cqre = JsonConvert.DeserializeObject<QGReportAlterList>(content);
                     List<UpdateRecord> records = new List<UpdateRecord>();
                    //if (cqre.totalPage != null && cqre.totalPage > 0)
                    //{
                      
                    //    for (int i = 1; i <= cqre.totalPage; i++)
                    //    {
                    //        List<RequestSetting> list = new List<RequestSetting>();
                    //        list.Add(new RequestSetting
                    //        {
                    //            Method = "get",
                    //            Url = "http://tj.gsxt.gov.cn" + document["alterUrl"] + "?entType=" + _entType,
                    //            IsArray = "0",
                    //            Name = "alter",
                    //            Data = string.Format("draw={0}&start={1}&length=5", i.ToString(), ((i - 1) * 5).ToString())
                    //        });
                    //        var responsel = _request.GetResponseInfo(list);
                    //        cqre = JsonConvert.DeserializeObject<QGReportAlterList>(responsel[0].Data);
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
                        Url = "http://tj.gsxt.gov.cn" + document["alterUrl"] + "?entType=" + _entType,
                        IsArray = "0",
                        Name = "alter",
                        //Data = "draw=1&start=0&length=5",
                    });
                    list.Add(new RequestSetting
                    {
                        Method = "get",
                        Url = "http://tj.gsxt.gov.cn" + document["webSiteInfoUrl"] + "?entType=" + _entType,
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
                    if (cqre != null)
                    {
                        //CQReportBaseInfo reportDetail = cqre.form;
                        Utility.ClearNullValue<QGReportList>(cqre);
                        report.name = cqre.entName;
                        if (report.name == "" && cqre.traName != null && cqre.traName != "")
                        {
                            report.name = cqre.traName;
                        }
                        if (cqre.uniscId != null && cqre.uniscId.Length == 15)
                            report.reg_no = cqre.uniscId;
                        if (cqre.uniscId != null && cqre.uniscId.Length == 18)
                            report.credit_no = cqre.uniscId;
                        if (cqre.regNo != null && cqre.regNo.Length == 15)
                            report.reg_no = cqre.regNo;
                        if (cqre.regNo != null && cqre.regNo.Length == 18)
                            report.credit_no = cqre.regNo;                        
                        report.telephone = cqre.tel;
                        report.address = cqre.addr;
                        report.zip_code = cqre.postalCode;
                        report.email = cqre.email;
                        report.if_external_guarantee = "否";
                        report.if_equity = "否";
                        report.if_invest = "否";
                        report.if_website = "否";
                        report.status = cqre.busSt_CN;
                        report.collegues_num = cqre.empNumDis == "1" ? cqre.empNum + "人" : "企业选择不公示";                        
                        report.total_equity = cqre.assGroDis == "1" ? cqre.assGroDis + "万元" : "企业选择不公示";                        
                        report.sale_income = cqre.vendIncDis == "1" ? cqre.vendInc + "万元" : "企业选择不公示";                      
                        report.serv_fare_income = cqre.maiBusIncDis == "1" ? cqre.maiBusInc + "万元" : "企业选择不公示";                        
                        report.tax_total = cqre.ratGroDis == "1" ? cqre.ratGro + "万元" : "企业选择不公示";                       
                        report.profit_reta = cqre.totEquDis == "1" ? cqre.totEqu + "万元" : "企业选择不公示";                       
                        report.profit_total = cqre.proGroDis == "1" ? cqre.proGro + "万元" : "企业选择不公示";                       
                        report.net_amount = cqre.netIncDis == "1" ? cqre.netInc + "万元" : "企业选择不公示";                        
                        report.debit_amount = cqre.liaGroDis == "1" ? cqre.liaGro + "万元" : "企业选择不公示";                   

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
                                    item.seq_no = partnerList.Count + 1;
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


        #region 解析动产抵押
        void LoadAndParseMortReg(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            this.LoadAndParseMortRegContent(responseData);
            var totalPage = document.Contains("totalPage") ? (document["totalPage"].IsBsonNull ? 1 : document["totalPage"].AsInt32) : 1;
            if (totalPage > 1)
            {
               // this.LoadAndParseMortRegByPage(totalPage);
            }
        }
        #endregion
        #region 解析动产抵押内容
        void LoadAndParseMortRegContent(string responseData)
        {
            QGDcdyList cqInfo = JsonConvert.DeserializeObject<QGDcdyList>(responseData);
            if (cqInfo.data != null && cqInfo.data.Length > 0)
            {
                for (int i = 0; i < cqInfo.data.Length; i++)
                {
                    QGDcdyInfo item = cqInfo.data[i];
                    Utility.ClearNullValue<QGDcdyInfo>(item);
                    MortgageInfo mortgageinfo = new MortgageInfo();
                    mortgageinfo.seq_no = _enterpriseInfo.mortgages.Count + 1;
                    mortgageinfo.number = item.morRegCNo;
                    mortgageinfo.date = string.IsNullOrEmpty(item.regiDate) ? "" : ConvertStringToDate(item.regiDate);
                    mortgageinfo.amount = string.IsNullOrEmpty(item.priClaSecAm) ? item.priClaSecAm : item.priClaSecAm + "万" + item.regCapCur_Cn;
                    mortgageinfo.status = item.type == "1" ? "有效" : item.type == "2" ? "无效" : "";
                    mortgageinfo.department = item.regOrg_CN;
                    _request.AddOrUpdateRequestParameter("dcdydetailId", item.morReg_Id);
                    List<XElement> requestList = null;
                    List<ResponseInfo> responseList = null;
                    requestList = _requestXml.GetRequestListByGroup("motage").ToList();
                 //   responseList = GetResponseInfo(requestList);
                  //  LoadAndParseMortgageDetailInfo(responseList, mortgageinfo);
                  //  list.Add(mortgageinfo);
                }
            }
        }
        #endregion

        #region 解析股权出质
        void LoadAndParseStakQualit(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            this.LoadAndParseStakQualitContent(responseData);
            var totalPage = document.Contains("totalPage") ? (document["totalPage"].IsBsonNull ? 1 : document["totalPage"].AsInt32) : 1;
            if (totalPage > 1)
            {
                this.LoadAndParseStakQualitByPage(totalPage);
            }
        }
        #endregion

        #region 解析股权出质内容
        void LoadAndParseStakQualitContent(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            var data = document.Contains("data") ? (document["data"].IsBsonNull ? null : document["data"].AsBsonArray) : null;
            if (data != null)
            {
                foreach (var item in data)
                {
                    EquityQuality equityquality = new EquityQuality();
                    equityquality.seq_no = _enterpriseInfo.equity_qualities.Count + 1;
                    equityquality.number = item["equityNo"].IsBsonNull ? string.Empty : item["equityNo"].AsString;
                    equityquality.pledgor = item["pledgor"].IsBsonNull ? string.Empty : item["pledgor"].AsString;
                    equityquality.pledgor_identify_no = item["pledBLicNo"]=="" ? "非公示项" : item["pledBLicNo"].AsString;
                    equityquality.pledgor_amount = item["impAm"].IsBsonNull ? string.Empty : (item["impAm"].IsInt32 ? item["impAm"].AsInt32.ToString() : item["impAm"].AsDouble.ToString());
                    equityquality.pawnee = item["impOrg"].IsBsonNull ? string.Empty : item["impOrg"].AsString;
                    equityquality.pawnee_identify_no = item["impOrgBLicNo"].IsBsonNull ? string.Empty : item["impOrgBLicNo"].AsString;
                    equityquality.date = item["equPleDate"].IsBsonNull ? "" : ConvertStringToDate(item["equPleDate"].AsInt64.ToString());
                    equityquality.status = item["type"] == "1" ? "有效" : item["type"] == "2" ? "无效" : "";
                    equityquality.public_date = item["publicDate"].IsBsonNull ? "" : ConvertStringToDate(item["publicDate"].AsInt64.ToString());
                    if (equityquality.pledgor_identify_no != "")
                    {
                        var detailData = item["vStakQualitInfoAlt"].IsBsonNull ? null : item["vStakQualitInfoAlt"].AsBsonArray;
                        if (detailData != null && detailData.Count > 0)
                        {
                            int seq = 1;
                            List<ChangeItem> its = new List<ChangeItem>();
                            foreach (var update in detailData)
                            {                               
                                ChangeItem it = new ChangeItem();
                                it.change_content = update["alt"].IsBsonNull ? string.Empty : update["alt"].AsString;
                                it.seq_no = seq;
                                it.change_date = update["altDate"].IsBsonNull ? string.Empty : ConvertStringToDate(update["altDate"].AsInt64.ToString());
                                its.Add(it);
                            }
                            equityquality.change_items = its;
                        }
                        _enterpriseInfo.equity_qualities.Add(equityquality);
                    }
                   
                }
            }
        }
        #endregion
        #region 解析股权出质----分页
        void LoadAndParseStakQualitByPage(int totalPage)
        {
            for (int i = 2; i <= totalPage; i++)
            {
                List<RequestSetting> list = new List<RequestSetting>();
                list.Add(new RequestSetting
                {
                    Method = "post",
                    Url = string.Format("{0}{1}", "http://tj.gsxt.gov.cn", _urls["stakQualitInfoUrl"]),
                    IsArray = "0",
                    Name = "stakQualit",
                    Data = string.Format("draw={0}&start={1}&length=5", i.ToString(), ((i - 1) * 5).ToString())
                });
                var responseList = _request.GetResponseInfo(list);
                if (responseList != null && responseList.Any())
                {
                    this.LoadAndParseStakQualitContent(responseList.First().Data);
                }
            }
        }
        #endregion

        #region 解析基本信息
        /// <summary>
        /// 解析基本信息
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseBasic(string responseData)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            var div = rootNode.SelectSingleNode("//div[@id='primaryInfo']/div[@class='details clearfix']/div[@class='overview']");
            if (div != null)
            {
                var dls = div.SelectNodes("./dl");
                if (dls != null && dls.Any())
                {
                    foreach (var dl in dls)
                    {
                        var title = dl.SelectSingleNode("./dt").InnerText.Replace("：", "").Replace(":","").Trim();
                        var val = dl.SelectSingleNode("./dd").InnerText.Replace("\r", "").Replace("\n", "")
                            .Replace("\t", "").Replace("null", "").Replace("NULL", "").Replace(" ", "");
                        switch (title)
                        {
                            case "登记证号":
                            case "注册号":
                                _enterpriseInfo.reg_no = val.Replace("<!--这里还需要添加业务逻辑-->", "");;
                                break;
                            case "统一社会信用代码":
                                _enterpriseInfo.credit_no = val.Replace("<!--这里还需要添加业务逻辑-->", "");;
                                break;
                            case "注册号/统一社会信用代码":
                            case "统一社会信用代码/注册号":
                                if (val.Replace("<!--这里还需要添加业务逻辑-->", "").Length == 18)
                                    _enterpriseInfo.credit_no = val.Replace("<!--这里还需要添加业务逻辑-->", "");
                                else if (val.Length < 18)
                                    _enterpriseInfo.reg_no = val.Replace("<!--这里还需要添加业务逻辑-->", "");
                                break;
                            case "事务所名称":
                            case "名称":
                            case "企业名称":
                                _enterpriseInfo.name = val.Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                                break;
                            case "组织形式":
                            case "类型":
                                _enterpriseInfo.econ_kind = val;
                                break;
                            case "法定代表人":
                            case "负责人":
                            case "股东":
                            case "经营者":
                            case "执行事务合伙人":
                            case "主任姓名":
                            case "投资人":
                                _enterpriseInfo.oper_name = val;
                                break;
                            case "住所":
                            case "经营场所":
                            case "营业场所":
                            case "主要经营场所":
                            case "办公地址":
                                Address address = new Address();
                                address.name = "注册地址";
                                address.address = val;
                                address.postcode = "";
                                _enterpriseInfo.addresses.Add(address);
                                break;
                            case "注册资金":
                            case "注册资本":
                            case "成员出资总额":
                                _enterpriseInfo.regist_capi = val;
                                break;
                            case "成立日期":
                            case "登记日期":
                            case "注册日期":
                            case "登记时间":
                                _enterpriseInfo.start_date = val;
                                break;
                            case "营业期限自":
                            case "经营期限自":
                            case "合伙期限自":
                                _enterpriseInfo.term_start = val;
                                break;
                            case "营业期限至":
                            case "经营期限至":
                            case "合伙期限至":
                                _enterpriseInfo.term_end = val;
                                break;
                            case "业务范围":
                            case "经营范围":
                                _enterpriseInfo.scope = val;
                                break;
                            case "发证机关":
                            case "登记机关":
                            case "主管单位":
                                _enterpriseInfo.belong_org = val;
                                break;
                            case "核准日期":
                                _enterpriseInfo.check_date = val;
                                break;
                            case "登记状态":
                                _enterpriseInfo.status = val;
                                break;
                            case "吊销日期":
                            case "注销日期":
                                _enterpriseInfo.end_date = val;
                                break;
                            case "组成形式":
                                _enterpriseInfo.type_desc = val;
                                break;
                            case "办公电话":
                            case "联系电话":
                                _enterpriseInfo.telephones = new List<ExtendValuePair>() { new ExtendValuePair("办公电话", val) };
                                break;
                            case "传真":
                                _enterpriseInfo.faxes = new List<ValuePair>() { new ValuePair("传真", val) };
                                break;
                            case "电子邮箱":
                                _enterpriseInfo.emails = new List<ExtendValuePair>() { new ExtendValuePair("电子邮箱", val) };
                                break;
                            case "聘用律师姓名":
                                _enterpriseInfo.employees = new List<Employee>() { new Employee() { name = val, seq_no = 0 } };
                                break;
                            default:
                                break;
                        }
                    }
                }
            }
        }
        #endregion

        #region 解析URL
        /// <summary>
        /// 解析URL
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseUrls(string responseData)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            var rootNode = document.DocumentNode;
            var entType = rootNode.SelectSingleNode("//input[@id='entType']");
            var script = rootNode.SelectSingleNode("//div[@id='url']/script");
            if (entType != null)
            {
                _entType = entType.Attributes["value"].Value;
            }
            if (script != null)
            {

                var url_arr = script.InnerHtml.Replace("\n", "").Replace("\t", "").Split('\r').Where(p => !string.IsNullOrWhiteSpace(p)).Where(p => !p.TrimStart().StartsWith("<!--")).ToList();
                if (url_arr.Any())
                {
                    foreach (var url in url_arr)
                    {
                        var inn_arr = url.TrimStart().Replace("var ","").Split('=');
                        if (inn_arr != null && inn_arr.Count() == 2)
                        {
                            if (!_urls.ContainsKey(inn_arr.First()))
                            {
                                _urls.Add(inn_arr.First().TrimEnd(), inn_arr.Last().TrimStart(new char[] { ' ', '\"' }).TrimEnd(new char[] { ';', '\"' }));
                            }
                        }
                    }
                }
            }
        }
        #endregion

        private List<ResponseInfo> GetResponseInfo(IEnumerable<XElement> elements)
        {
            List<ResponseInfo> responseList = new List<ResponseInfo>();
            foreach (XElement el in elements)
            {
                responseList.Add(this._request.RequestData(el));
            }

            return responseList;
        }
        #region 构建请求列表
        List<RequestSetting> GetRequestSettings()
        {
            var domain = "http://tj.gsxt.gov.cn";
            List<RequestSetting> list = new List<RequestSetting>();
            if (_urls.Any())
            {
                foreach (var url in _urls)
                {
                    
                    if (url.Key == "shareholderUrl")
                    {
                        if (_entType != "16")
                        {
                            list.Add(new RequestSetting
                            {
                                Method = "post",
                                Url = domain + url.Value,
                                IsArray = "0",
                                Name = "partner",
                                Data = "draw=1&start=0&length=5"
                            });
                        }
                       
                    }
                    else if (url.Key == "keyPersonUrl")
                    {                        
                        if (_entType != "16")
                        {
                            list.Add(new RequestSetting
                            {
                                Method = "get",
                                Url = domain + url.Value,
                                IsArray = "0",
                                Name = "employee"

                            });
                        }
                       
                    }
                    else if (url.Key == "gtAlertInfoUrl")
                    {
                        if (_entType == "16")
                        {
                            list.Add(new RequestSetting
                            {
                                Method = "get",
                                Url = domain + url.Value,
                                IsArray = "0",
                                Name = "changerecord"

                            });
                        }
                    }
                    else if (url.Key == "alterInfoUrl")
                    {
                        if (_entType != "16")
                        {
                            list.Add(new RequestSetting
                            {
                                Method = "post",
                                Url = domain + url.Value,
                                IsArray = "0",
                                Name = "changerecord",
                                Data = "draw=1&start=0&length=5"
                            });
                        }
                    }
                    else if (url.Key == "branchUrl")
                    {
                        if (_entType != "16")
                        {
                            list.Add(new RequestSetting
                            {
                                Method = "get",
                                Url = domain + url.Value,
                                IsArray = "0",
                                Name = "branch"
                            });
                        }
                    }
                    else if (url.Key == "stakQualitInfoUrl")
                    {
                        if (_entType != "16" && _entType != "17" && _entType != "18")
                        {
                            list.Add(new RequestSetting
                               {
                                   Method = "get",
                                   Url = domain + url.Value,
                                   IsArray = "0",
                                   Name = "stakQualit",
                                   //  Data = "draw=1&start=0&length=5"
                               });
                        }
                    }
                    else if (url.Key == "mortRegInfoUrl")
                    {
                        list.Add(new RequestSetting
                        {
                            Method = "get",
                            Url = domain + url.Value,
                            IsArray = "0",
                            Name = "mortReg",
                          //  Data = "draw=1&start=0&length=5"
                        });
                    }
                    else if (url.Key == "anCheYearInfo")
                    {
                        list.Add(new RequestSetting
                        {
                            Method = "get",
                            Url = domain + url.Value,
                            IsArray = "0",
                            Name = "report",
                            //  Data = "draw=1&start=0&length=5"
                        });
                    }
                    else if (url.Key == "indBusExcepUrl")
                    {
                        if (_entType == "16")
                        {
                            list.Add(new RequestSetting
                            {
                                Method = "get",
                                Url = domain + url.Value,
                                IsArray = "0",
                                Name = "abnormal",
                                //  Data = "draw=1&start=0&length=5"
                            });
                        }
                    }
                    else if (url.Key == "entBusExcepUrl")
                    {
                        if (_entType != "16" && _entType != "17" && _entType != "18")
                        {
                            list.Add(new RequestSetting
                            {
                                Method = "get",
                                Url = domain + url.Value,
                                IsArray = "0",
                                Name = "abnormal",
                                //  Data = "draw=1&start=0&length=5"
                            });
                        }
                    }
                    else if (url.Key == "assistUrl")
                    {
                        if (_entType != "16" && _entType != "17" && _entType != "18")
                        {
                            list.Add(new RequestSetting
                            {
                                Method = "get",
                                Url = domain + url.Value,
                                IsArray = "0",
                                Name = "freeze",
                                //  Data = "draw=1&start=0&length=5"
                            });
                        }
                    }
                    else if (url.Key == "argBusExcepUrl")
                    {
                        if (_entType == "16")
                        {
                            list.Add(new RequestSetting
                            {
                                Method = "get",
                                Url = domain + url.Value,
                                IsArray = "0",
                                Name = "abnormal",
                                //  Data = "draw=1&start=0&length=5"
                            });
                        }
                    }
                    else if (url.Key == "gtKeyPersonUrl")
                    {
                        if (_entType == "16")
                        {
                            list.Add(new RequestSetting
                            {
                                Method = "get",
                                Url = domain + url.Value,
                                IsArray = "0",
                                Name = "employee",
                                //  Data = "draw=1&start=0&length=5"
                            });
                        }
                    }
                    else if (url.Key == "argBusExcepUrl")
                    {
                        if (_entType == "17")
                        {
                            list.Add(new RequestSetting
                            {
                                Method = "get",
                                Url = domain + url.Value,
                                IsArray = "0",
                                Name = "abnormal",
                                //  Data = "draw=1&start=0&length=5"
                            });
                        }
                    }
                    
                }
            }
            return list;
        }
        #endregion

        #region 解析股东信息
        void LoadAndParsePartner(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            this.LoadAndParsePartnerContent(responseData);
            var totalPage = document.Contains("totalPage") ? (document["totalPage"].IsBsonNull ? 1 : document["totalPage"].AsInt32) : 1;
            if (totalPage > 1)
            {
                this.LoadAndParsePartnerByPage(totalPage);
            }

        }
        #endregion

        #region 解析股东信息--分页
        /// <summary>
        /// 解析股东信息--分页
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParsePartnerByPage(int totalPage)
        {
            for (int i = 2; i <= totalPage; i++)
            {
                List<RequestSetting> list = new List<RequestSetting>();
                list.Add(new RequestSetting
                {
                    Method = "post",
                    Url = string.Format("{0}{1}", "http://tj.gsxt.gov.cn", _urls["shareholderUrl"]),
                    IsArray = "0",
                    Name = "partner",
                    Data = string.Format("draw={0}&start={1}&length=5", i.ToString(), ((i - 1) * 5).ToString())
                });
                var responseList = _request.GetResponseInfo(list);
                if (responseList != null && responseList.Any())
                {
                    this.LoadAndParsePartnerContent(responseList.First().Data);
                }
            }
        }
        #endregion

        #region 解析股东信息内容
        /// <summary>
        /// 解析股东信息内容
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParsePartnerContent(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            var data = document.Contains("data") ? (document["data"].IsBsonNull ? null : document["data"].AsBsonArray) : null;
            if (data != null)
            {
                foreach (var item in data)
                {
                    Partner partner = new Partner();
                    partner.seq_no = _enterpriseInfo.partners.Count + 1;
                    if (_entType == "1" || _entType == "10" || _entType == "101" || _entType == "1001" ||_entType == "5")
                    {
                        var cerType_CN = item["cerType_CN"].IsBsonNull ? string.Empty : item["cerType_CN"].AsString;
                        var blicType_CN = item["blicType_CN"].IsBsonNull ? string.Empty : item["blicType_CN"].AsString;
                        var bLicNo = item["bLicNo"].IsBsonNull ? string.Empty : item["bLicNo"].AsString;
                        var inv = item["inv"].IsBsonNull ? string.Empty : item["inv"].AsString;
                        var detailCheck = item["detailCheck"].IsBsonNull ? string.Empty : item["detailCheck"].AsString;
                        var invId = item["invId"].IsBsonNull ? string.Empty : item["invId"].AsString;
                        var invType_CN = item["invType_CN"].IsBsonNull ? string.Empty : item["invType_CN"].AsString;
                        partner.identify_type = !string.IsNullOrWhiteSpace(cerType_CN) ? "非公示项"
                            : (string.IsNullOrWhiteSpace(cerType_CN) && !string.IsNullOrWhiteSpace(blicType_CN)) ? blicType_CN.Trim() : "非公示项";
                        partner.identify_no = !string.IsNullOrWhiteSpace(cerType_CN) ? "非公示项"
                            : (string.IsNullOrWhiteSpace(cerType_CN) && !string.IsNullOrWhiteSpace(blicType_CN)) ? this.CleanHtmlNodes(bLicNo) : "非公示项";
                        partner.stock_type = this.CleanHtmlNodes(invType_CN);
                        partner.stock_name = inv;
                        partner.total_real_capi = item["liAcConAm"].IsBsonNull ? string.Empty : item["liAcConAm"].ToString();
                        partner.total_should_capi = item["liSubConAm"].IsBsonNull ? string.Empty : item["liSubConAm"].ToString();
                        if (detailCheck == "true")
                        {
                            List<RequestSetting> list = new List<RequestSetting>();
                            list.Add(new RequestSetting
                            {
                                Method = "get",
                                Url = string.Format("http://tj.gsxt.gov.cn/corp-query-entprise-info-shareholderDetail-{0}.html", invId),
                                IsArray = "0",
                                Name = "partner_detail"
                            });
                            var responseList = _request.GetResponseInfo(list);
                            if (responseList != null && responseList.Any())
                            {
                                this.LoadAndParsePartnerDetail(responseList.First().Data, partner);
                            }
                        }
                    }
                    else
                    {
                        if (_entType == "7")
                        {
                            var inv = item["inv"].IsBsonNull ? string.Empty : item["inv"].AsString;
                            //var sConForm_CN = item["sConForm_CN"].IsBsonNull ? string.Empty : item["sConForm_CN"].AsString;
                            partner.stock_name = inv;
                            //partner.stock_type = sConForm_CN;
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(partner.stock_name))
                    {
                        _enterpriseInfo.partners.Add(partner);
                    }
                    
                }
            }
        }
        #endregion

        #region 解析股东信息详情
        /// <summary>
        /// 解析股东信息详情
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParsePartnerDetail(string responseData,Partner partner)
        {
            if (string.IsNullOrWhiteSpace(responseData)) return;
            BsonDocument document = BsonDocument.Parse(responseData);
            var data = document.Contains("data") ? (document["data"].IsBsonNull ? null : document["data"].AsBsonArray) : null;
            if (data != null && data.Count == 2)
            {
                BsonArray real_arr = data.First() as BsonArray;
                BsonArray should_arr = data.Last() as BsonArray;
                ShouldCapiItem sci = new ShouldCapiItem();
                if (should_arr != null && should_arr.Any())
                {
                    var item = should_arr.First();
                    sci.invest_type = item["conForm_CN"].IsBsonNull ? string.Empty : item["conForm_CN"].AsString;
                    sci.shoud_capi = item["subConAm"].IsBsonNull ? string.Empty : (item["subConAm"].IsInt32 ? item["subConAm"].AsInt32.ToString() : item["subConAm"].AsDouble.ToString());
                    sci.should_capi_date = item["conDate"].IsBsonNull?string.Empty:this.ConvertStringToDate(item["conDate"].AsInt64.ToString());
                    partner.should_capi_items.Add(sci);
                }
                if (real_arr != null && real_arr.Any())
                {
                    var item = real_arr.First();
                    RealCapiItem rci = new RealCapiItem();
                    rci.invest_type = item["conForm_CN"].IsBsonNull ? string.Empty : item["conForm_CN"].AsString;
                    rci.real_capi = item["acConAm"].IsBsonNull ? string.Empty : (item["acConAm"].IsInt32 ? item["acConAm"].AsInt32.ToString() : item["acConAm"].AsDouble.ToString());
                    rci.real_capi_date = item["conDate"].IsBsonNull ? string.Empty : this.ConvertStringToDate(item["conDate"].AsInt64.ToString());
                    partner.real_capi_items.Add(rci);
                }
            }
        }
        #endregion

        #region 解析分支机构
        /// <summary>
        /// 解析分支机构
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseBranch(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            var data = document.Contains("data") ? (document["data"].IsBsonNull ? null : document["data"].AsBsonArray) : null;
            if (data != null)
            {
                foreach (var item in data)
                {
                    Branch branch = new Branch();
                    branch.seq_no = _enterpriseInfo.branches.Count + 1;
                    branch.name = item["brName"].IsBsonNull ? string.Empty : item["brName"].AsString;
                    branch.reg_no = item["regNo"].IsBsonNull ? string.Empty : item["regNo"].AsString;
                    branch.belong_org = item["regOrg_CN"].IsBsonNull ? string.Empty : item["regOrg_CN"].AsString;
                    _enterpriseInfo.branches.Add(branch);
                }
            }
        }
        #endregion

        #region 解析主要人员
        /// <summary>
        /// 解析主要人员
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseEmployee(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            var data = document.Contains("data") ? (document["data"].IsBsonNull ? null : document["data"].AsBsonArray) : null;
            if (data != null)
            {
                foreach (var item in data)
                {
                    Employee employee = new Employee();
                    employee.seq_no = _enterpriseInfo.employees.Count + 1;
                    HtmlDocument html = new HtmlDocument();
                    html.LoadHtml(item["name"].AsString);
                    var rootNode = html.DocumentNode;
                    var texts = rootNode.ChildNodes.Where(p=>p.Name=="#text");
                    if (texts != null && texts.Any())
                    {
                        StringBuilder sb = new StringBuilder();
                        foreach (var text in texts)
                        {
                            sb.Append(text.InnerText);
                        }
                        employee.name = sb.ToString();
                    }
                    html.LoadHtml(item["position_CN"].AsString);
                    rootNode = html.DocumentNode;
                    var img = rootNode.SelectSingleNode("./img");
                    if (img != null)
                    { 
                        var src=img.Attributes["src"].Value;
                        if (_employeePosition.ContainsKey(src))
                        {
                            employee.job_title = _employeePosition[src];
                        }
                    }
                    
                    _enterpriseInfo.employees.Add(employee);
                }
            }
        }

        #endregion

        #region 解析变更信息
        /// <summary>
        /// 解析变更信息
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseChangeRecord(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            this.LoadAndParseChangeRecordContent(responseData);
            var totalPage = document.Contains("totalPage") ? (document["totalPage"].IsBsonNull ? 1 : document["totalPage"].AsInt32) : 1;
            if (totalPage > 1)
            {
                this.LoadAndParseChangeRecordByPage(totalPage);
            }
        }
        #endregion

        #region 解析变更信息--分页
        /// <summary>
        /// 解析变更信息--分页
        /// </summary>
        /// <param name="totalPage"></param>
        void LoadAndParseChangeRecordByPage(int totalPage)
        {
            for (int i = 2; i <= totalPage; i++)
            {
                List<RequestSetting> list = new List<RequestSetting>();
                list.Add(new RequestSetting
                        {
                            Method = "post",
                            Url = string.Format("{0}{1}", "http://tj.gsxt.gov.cn", _urls["alterInfoUrl"]),
                            IsArray = "0",
                            Name = "changerecord",
                            Data = string.Format("draw={0}&start={1}&length=5", i.ToString(), ((i - 1) * 5).ToString())
                        });
                var responseList = _request.GetResponseInfo(list);
                if (responseList != null && responseList.Any())
                {
                    this.LoadAndParseChangeRecordContent(responseList.First().Data);
                }
            }
        }
        #endregion

        #region 解析变更信息内容
        /// <summary>
        /// 解析变更信息内容
        /// </summary>
        /// <param name="responseData"></param>
        void LoadAndParseChangeRecordContent(string responseData)
        {
            BsonDocument document = BsonDocument.Parse(responseData);
            var data = document.Contains("data") ? (document["data"].IsBsonNull ? null : document["data"].AsBsonArray) : null;
            if (data != null)
            {
                foreach (var item in data)
                {
                    ChangeRecord cr = new ChangeRecord();
                    cr.seq_no = _enterpriseInfo.changerecords.Count + 1;
                    HtmlDocument html = new HtmlDocument();
                    html.LoadHtml(item["altItem_CN"].AsString);
                    var rootNode = html.DocumentNode;
                    var texts = rootNode.ChildNodes.Where(p => p.Name == "#text");
                    if (texts != null && texts.Any())
                    {
                        StringBuilder sb = new StringBuilder();
                        foreach (var text in texts)
                        {
                            sb.Append(text.InnerText);
                        }
                        cr.before_content = item["altBe"].AsString;
                        cr.after_content = item["altAf"].AsString;
                        cr.change_date = this.ConvertStringToDate(item["altDate"].AsInt64.ToString());
                        cr.change_item = sb.ToString();
                    }
                    _enterpriseInfo.changerecords.Add(cr);
                }
            }
        }
        #endregion

        #region CleanHtmlNodes
        string CleanHtmlNodes(string html)
        {
            StringBuilder sb = new StringBuilder();
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(html);
            var rootNode = document.DocumentNode;
            var texts = rootNode.ChildNodes.Where(p => p.Name == "#text");
            if (texts != null && texts.Any())
            {

                foreach (var text in texts)
                {
                    sb.Append(text.InnerText);
                }
            }
            return sb.ToString();
        }
        #endregion

        /// <summary>
        /// 解析股东详情
        /// </summary>
        /// <param name="partner"></param>
        /// <param name="responseData"></param>
        private void LoadAndParseInvestorDetails(Partner partner, String responseData)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);

            HtmlNode rootNode = document.DocumentNode;
            HtmlNode infoTable = rootNode.SelectSingleNode("//table[@class='result-table']");
            if (infoTable != null)
            {
                HtmlNodeCollection trList = infoTable.SelectNodes("//tr");

                if (trList != null && trList.Count > 3)
                {
                    for (int i = 3; i < trList.Count(); i++)
                    {
                        HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                        ShouldCapiItem sItem = new ShouldCapiItem();
                        RealCapiItem rItem = new RealCapiItem();

                        if (tdList != null && tdList.Count == 9)
                        {
                            sItem.invest_type = tdList[3].InnerText.Trim();
                            var shouldCapi9 = string.IsNullOrEmpty(tdList[4].InnerText.Trim()) ? tdList[1].InnerText.Trim() : tdList[4].InnerText.Trim();
                            sItem.shoud_capi = string.IsNullOrEmpty(shouldCapi9) ? "" : shouldCapi9 ;
                            sItem.should_capi_date = tdList[5].InnerText.Trim();
                            rItem.invest_type = tdList[6].InnerText.Trim();
                            rItem.real_capi_date = tdList[7].InnerText.Trim();
                            var realCapi9 = string.IsNullOrEmpty(tdList[8].InnerText.Trim()) ? tdList[2].InnerText.Trim() : tdList[8].InnerText.Trim();
                            rItem.real_capi = string.IsNullOrEmpty(realCapi9) ? "" : realCapi9 ;
                        }
                        else if (tdList != null && tdList.Count == 6)
                        {
                            sItem.invest_type = tdList[0].InnerText.Trim();
                            var sCapi = tdList[1].InnerText.Trim();
                            sItem.shoud_capi = string.IsNullOrEmpty(sCapi) ? "" : sCapi ;
                            sItem.should_capi_date = tdList[2].InnerText.Trim();
                            rItem.invest_type = tdList[3].InnerText.Trim();
                            var rCapi = tdList[4].InnerText.Trim();
                            rItem.real_capi = string.IsNullOrEmpty(rCapi) ? "" : rCapi;
                            rItem.real_capi_date = tdList[5].InnerText.Trim();
                        }

                        if (!String.IsNullOrEmpty(sItem.shoud_capi)) partner.should_capi_items.Add(sItem);
                        if (!String.IsNullOrEmpty(rItem.real_capi)) partner.real_capi_items.Add(rItem);
                    }
                }
            }
        }

        /// <summary>
        /// 解析备案信息:主要人员、分支机构
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseBeiAn(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            HtmlNodeCollection tables = rootNode.SelectNodes("//table[@class='result-table']");
            if (tables != null)
            {
                foreach (HtmlNode table in tables)
                {
                    if (table.InnerText.Contains("主管部门（出资人）信息"))
                    {
                        List<Partner> partnerList = new List<Partner>();

                        HtmlNodeCollection trList = table.SelectNodes("./tr");
                        if (trList!=null&&trList.Count>1)
                        {
                            for (int i = 1; i < trList.Count;i++ )
                            {
                                HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                                if (tdList != null && tdList.Count > 4)
                                {
                                    Partner partner = new Partner();
                                    partner.stock_type = tdList[1].InnerText.Trim();
                                    partner.stock_name = tdList[2].InnerText.Trim();
                                    partner.identify_type = tdList[3].InnerText.Trim();
                                    partner.identify_no = tdList[4].InnerText.Trim();
                                    partner.seq_no = i;
                                    partner.stock_percent = "";
                                    partner.should_capi_items = new List<ShouldCapiItem>();
                                    partner.real_capi_items = new List<RealCapiItem>();
                                    partnerList.Add(partner);
                                }
                            }
                        }
                        _enterpriseInfo.partners = partnerList;
                    }
                    else if (table.InnerText.Contains("主要人员信息"))
                    {
                        List<Employee> employeeList = new List<Employee>();

                        HtmlNodeCollection trList = table.SelectNodes("./tr");
                        if (trList != null && trList.Count > 1)
                        {
                            for (int i = 1; i < trList.Count; i++)
                            {
                                HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                                if (tdList != null && tdList.Count > 2)
                                {
                                    Employee employee = new Employee();
                                    employee.seq_no = Int32.Parse(tdList[0].InnerText.Trim());
                                    employee.name = tdList[1].InnerText.Trim();
                                    employee.job_title = tdList[2].InnerText.Trim();
                                    employee.sex = "";
                                    employee.cer_no = "";

                                    employeeList.Add(employee);

                                    if (tdList.Count > 5 && tdList[4].InnerText.Trim() != "")
                                    {
                                        Employee employee2 = new Employee();
                                        employee2.seq_no = Int32.Parse(tdList[3].InnerText.Trim());
                                        employee2.name = tdList[4].InnerText.Trim();
                                        employee2.job_title = tdList[5].InnerText.Trim();
                                        employee2.sex = "";
                                        employee2.cer_no = "";

                                        employeeList.Add(employee2);
                                    }
                                }
                            }
                        }
                        _enterpriseInfo.employees = employeeList;
                    }
                    else if (table.InnerText.Contains("分支机构信息"))
                    {
                        List<Branch> branchList = new List<Branch>();

                        HtmlNodeCollection trList = table.SelectNodes("./tr");
                        if (trList != null && trList.Count > 2)
                        {
                            for (int i = 2; i < trList.Count; i++)
                            {
                                HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                                if (tdList != null && tdList.Count > 3)
                                {
                                    Branch branch = new Branch();
                                    branch.seq_no = Int32.Parse(tdList[0].InnerText.Trim());
                                    branch.reg_no = tdList[1].InnerText.Trim();
                                    branch.name = tdList[2].InnerText.Trim();
                                    branch.belong_org = tdList[3].InnerText.Trim();
                                    branch.oper_name = "";

                                    branchList.Add(branch);
                                }
                            }
                        }
                        _enterpriseInfo.branches = branchList;
                    }
                }
            }
        }

        /// <summary>
        /// 解析经营异常信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseAbnormalItems(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            HtmlNodeCollection tables = rootNode.SelectNodes("//table[@class='result-table']");
            if (tables != null)
            {
                int year;
                foreach (HtmlNode table in tables)
                {
                    string header = table.SelectNodes("./tr/td")[0].InnerText.Trim();
                    if (header.StartsWith("经营异常信息"))
                    {
                        HtmlNodeCollection trList = table.SelectNodes("./tr");
                        if (trList != null && trList.Count > 2)
                        {
                            for (int i = 2; i < trList.Count; i++)
                            {
                                HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                                if (tdList != null && tdList.Count > 3)
                                {
                                    AbnormalInfo item = new AbnormalInfo();
                                    item.name = _enterpriseInfo.name;
                                    item.reg_no = _enterpriseInfo.reg_no;
                                    item.province = _enterpriseInfo.province;
                                    item.in_reason = tdList[1].InnerText.Trim();
                                    item.in_date = tdList[2].InnerText.Trim();
                                    if (int.TryParse(tdList[3].InnerText.Trim(), out year) || tdList.Count==7)
                                    {
                                        item.in_reason = item.in_reason + (string.IsNullOrWhiteSpace(tdList[3].InnerText.Trim()) ? "" : string.Format("【{0}】", tdList[3].InnerText.Trim()));
                                        item.out_reason = tdList[4].InnerText.Trim();
                                        item.out_date = tdList[5].InnerText.Trim();
                                        item.department = tdList[6].InnerText.Trim();
                                    }
                                    else
                                    {
                                        item.out_reason = tdList[3].InnerText.Trim();
                                        item.out_date = tdList[4].InnerText.Trim();
                                        item.department = tdList[5].InnerText.Trim();
                                    }

                                    this._abnormals.Add(item);
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 解析抽查检查信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseCheckUpItems(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

             HtmlNodeCollection tables = rootNode.SelectNodes("//table[@class='result-table']");
             if (tables != null)
             {
                 foreach (HtmlNode table in tables)
                 {
                     string header = table.SelectNodes("./tr/td")[0].InnerText.Trim();
                     if (header.StartsWith("抽查检查信息"))
                     {
                         HtmlNodeCollection trList = table.SelectSingleNode("./tbody[@id='tableChoucha']").SelectNodes("./tr");
                         if (trList != null && trList.Count > 1)
                         {
                             for (int i = 1; i < trList.Count; i++)
                             {
                                 HtmlNodeCollection tdList = trList[i].SelectNodes("./td");

                                 if (tdList != null && tdList.Count > 3)
                                 {
                                     CheckupInfo item = new CheckupInfo();
                                     item.name = _enterpriseInfo.name;
                                     item.reg_no = _enterpriseInfo.reg_no;
                                     item.province = _enterpriseInfo.province;
                                     item.department = tdList[1].InnerText.Trim();
                                     item.type = tdList[2].InnerText.Trim();
                                     item.date = tdList[3].InnerText.Trim();
                                     item.result = tdList[4].InnerText.Trim();

                                     _checkups.Add(item);
                                 }
                             }
                         }
                     }
                 }
             }
        }
        
        #region 解析股东及出资信息
        /// <summary>
        /// 解析股东及出资信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseFinancialContribution(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            List<FinancialContribution> _FinancialList = new List<FinancialContribution>();//股东出资
             HtmlNodeCollection tables = rootNode.SelectNodes("//table[@class='result-table']");
             if (tables != null)
             {
                 foreach (HtmlNode table in tables)
                 {
                     string header = table.SelectNodes("./tr/td")[0].InnerText.Trim();
                     if (header!=null&&header.StartsWith("股东及出资信息"))
                     {
                         HtmlNodeCollection trList = table.SelectNodes("./tr");
                         if (trList != null && trList.Count > 2)
                         {
                             FinancialContribution item;
                             for (int i = 2; i < trList.Count; i++)
                             {
                                 HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                                 if (tdList != null && tdList.Count > 3)
                                 {
                                     item = _FinancialList.Where(p => p.investor_name.Replace('(', '（').Replace(')', '）') == tdList[0].InnerText.Trim().Replace('(', '（').Replace(')', '）')).FirstOrDefault();
                                     if (item != null)
                                     {
                                         if (tdList[1].InnerText.Trim().Contains("实缴"))
                                         {
                                             FinancialContribution.RealCapiItem Capitem = new FinancialContribution.RealCapiItem();
                                             Capitem.real_invest_date = tdList[2].InnerText.Trim();
                                             Capitem.real_invest_type = tdList[3].InnerText.Trim();
                                             Capitem.real_capi = tdList[4].InnerText.Trim();
                                             Capitem.public_date = tdList[5].InnerText.Trim();
                                             item.real_capi_items.Add(Capitem);
                                         }
                                         else
                                         {
                                             FinancialContribution.ShouldCapiItem Shoulditem = new FinancialContribution.ShouldCapiItem();
                                             Shoulditem.should_invest_date = tdList[2].InnerText.Trim();
                                             Shoulditem.should_invest_type = tdList[3].InnerText.Trim();
                                             Shoulditem.should_capi = tdList[4].InnerText.Trim();
                                             Shoulditem.public_date = tdList[5].InnerText.Trim();
                                             item.should_capi_items.Add(Shoulditem);
                                         }
                                     }
                                     else
                                     {
                                         item = new FinancialContribution();
                                         BuildFinancialEntity(_FinancialList, item, tdList);
                                         _FinancialList.Add(item);
                                     }
                                 }
                             }
                         }
                     }
                 }
             }
             _enterpriseInfo.financial_contributions = _FinancialList;
        }
        #region Build Financial实体
        /// <summary>
        /// Build Financial实体
        /// </summary>
        /// <param name="_FinancialList"></param>
        /// <param name="item"></param>
        /// <param name="tdList"></param>
        private void BuildFinancialEntity(List<FinancialContribution> _FinancialList, FinancialContribution item, HtmlNodeCollection tdList)
        {
            item.seq_no = _FinancialList.Count + 1;
            item.investor_name = tdList[0].InnerText.Trim();
            item.investor_type = tdList[1].InnerText.Trim();
            if (tdList[1].InnerText.Trim().Contains("实缴"))
            {
                List<FinancialContribution.RealCapiItem> real_capi_items = new List<FinancialContribution.RealCapiItem>();
                FinancialContribution.RealCapiItem Capitem = new FinancialContribution.RealCapiItem();
                Capitem.real_invest_date = tdList[2].InnerText.Trim();
                Capitem.real_invest_type = tdList[3].InnerText.Trim();
                Capitem.real_capi = tdList[4].InnerText.Trim();
                real_capi_items.Add(Capitem);
                item.real_capi_items = real_capi_items;
            }
            else
            {
                List<FinancialContribution.ShouldCapiItem> should_capi_items = new List<FinancialContribution.ShouldCapiItem>();
                FinancialContribution.ShouldCapiItem Shoulditem = new FinancialContribution.ShouldCapiItem();
                Shoulditem.should_invest_date = tdList[2].InnerText.Trim();
                Shoulditem.should_invest_type = tdList[3].InnerText.Trim();
                Shoulditem.should_capi = tdList[4].InnerText.Trim();
                should_capi_items.Add(Shoulditem);
                item.should_capi_items = should_capi_items;
            }
        }
        #endregion
        #endregion

        #region 解析股权变更信息
        /// <summary>
        /// 解析股权变更信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseStockChange(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            List<StockChangeItem> stock_changes = new List<StockChangeItem>();//股权变更
            HtmlNodeCollection tables = rootNode.SelectNodes("//table[@class='result-table']");
            if (tables != null)
            {
                foreach (HtmlNode table in tables)
                {
                    string header = table.SelectNodes("./tr/td")[0].InnerText.Trim();
                    if (header.StartsWith("股权变更信息"))
                    {
                        HtmlNodeCollection trList = table.SelectNodes("./tr");
                        if (trList != null && trList.Count > 2)
                        {
                            for (int i = 2; i < trList.Count; i++)
                            {
                                HtmlNodeCollection tdList = trList[i].SelectNodes("./td");

                                if (tdList != null && tdList.Count > 3)
                                {

                                    StockChangeItem item = new StockChangeItem();
                                    item.seq_no = stock_changes.Count+1;
                                    item.name = tdList[0].InnerText.Trim();
                                    item.before_percent = tdList[1].InnerText.Trim();
                                    item.after_percent = tdList[2].InnerText.Trim();
                                    item.change_date = tdList[3].InnerText.Trim();
                                    item.public_date = tdList[4].InnerText.Trim();
                                    stock_changes.Add(item);
                                }
                            }
                        }
                    }
                }
            }
            _enterpriseInfo.stock_changes = stock_changes;
        }
        #endregion

        #region 解析动产抵押登记信息
        /// <summary>
        /// 解析动产抵押登记信息
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseMortgageInfo(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            List<MortgageInfo> MortgageList = new List<MortgageInfo>();

            HtmlNodeCollection tables = rootNode.SelectNodes("//table[@class='result-table']");
            if (tables != null)
            {
                foreach (HtmlNode table in tables)
                {
                    string header = table.SelectNodes("./tr/td")[0].InnerText.Trim();
                    if (header.StartsWith("动产抵押登记信息"))
                    {
                        HtmlNodeCollection trList = table.SelectNodes("./tr");
                        if (trList != null && trList.Count > 2)
                        {
                            for (int i = 2; i < trList.Count; i++)
                            {
                                HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                                if (tdList != null && tdList.Count > 2)
                                {
                                    MortgageInfo Mortgage = new MortgageInfo();
                                    Mortgage.seq_no = MortgageList.Count+1;
                                    Mortgage.number = tdList[1].InnerText.Trim();
                                    Mortgage.date = tdList[2].InnerText.Trim();
                                    Mortgage.amount = tdList[4].InnerText.Trim();
                                    Mortgage.department = tdList[3].InnerText.Trim();
                                    Mortgage.status = tdList[5].InnerText.Trim();
                                    Mortgage.public_date = tdList[6].InnerText.Trim();
                                    MortgageList.Add(Mortgage);
                                    var dtl = tdList[6].SelectSingleNode("./a");
                                    if (dtl != null)
                                    {
                                        var ID = dtl.Attributes["onclick"].Value.Split('(')[1].Split(',')[0].Replace("'", "").ToString().Trim();
                                        // 解析动产抵押登记详情
                                        if (!string.IsNullOrEmpty(ID))
                                        {
                                            var request = CreateRequest();
                                            request.AddOrUpdateRequestParameter("ID", ID);
                                            var xml = _requestXml.GetRequestListByName("diya_detials");
                                            List<ResponseInfo> reponseList = request.GetResponseInfo(xml);
                                            if (reponseList.Count() > 0)
                                            {
                                                LoadAndParseMortgageDetails(Mortgage, reponseList[0].Data);
                                            }
                                        }
                                    }
                                }

                            }
                        }
                    }
                }
            }
            _enterpriseInfo.mortgages = MortgageList;
        }
        /// <summary>
        /// 解析动产抵押登记详情
        /// </summary>
        /// <param name="punish"></param>
        /// <param name="responseData"></param>
        private void LoadAndParseMortgageDetails(MortgageInfo mortgage, String responseData)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);

            HtmlNode rootNode = document.DocumentNode;
            HtmlNodeCollection infoTable = rootNode.SelectNodes("//table");
            if (infoTable != null)
            {
                foreach (HtmlNode table in infoTable)
                {
                    string header = table.SelectNodes("./tr/td")[0].InnerText.Trim();
                    //补齐tr标签
                    if (table.SelectNodes("./td") != null)
                    {
                        var cells = table.SelectNodes("./td");
                        for (int index = 0; index < cells.Count / 5; index++)
                        {
                            var tr = table.AppendChild(HtmlNode.CreateNode("<tr></tr>"));
                            for (int tdindex = index * 5; tdindex < (index + 1) * 5; tdindex++)
                            {
                                tr.AppendChild(cells[tdindex]);
                            }
                        }
                        foreach (var td in cells)
                        {
                            table.RemoveChild(td);
                        }
                    }
                    HtmlNodeCollection trList = table.SelectNodes("./tr");
                    if (header.StartsWith("动产抵押登记信息"))
                    {
                        if (trList != null && trList.Count >1)
                        {
                            for (int i = 1; i < trList.Count; i++)
                            {
                                HtmlNodeCollection tdList = trList[i].SelectNodes("./td");

                                for (int j = 0; j < tdList.Count; j++)
                                {
                                    switch (tdList[j].InnerText.Trim())
                                    {
                                        case "债务人履行债务的期限":
                                            mortgage.period = tdList[j + 1].InnerText.Trim();
                                            break;
                                        case "被担保债权种类":
                                            mortgage.debit_type = tdList[j + 1].InnerText.Trim();
                                            break;
                                        case "担保范围":
                                            mortgage.scope = tdList[j + 1].InnerText.Trim();
                                            break;
                                        case "备注":
                                            mortgage.remarks = tdList[j + 1].InnerText.Trim();
                                            break;
                                    }
                                }
                            }
                        }
                    }
                    else if (header.StartsWith("抵押权人概况"))
                    {
                        List<Mortgagee> mortgagees = new List<Mortgagee>();
                        if (trList != null && trList.Count > 2)
                        {
                            for (int i = 2; i < trList.Count; i++)
                            {
                                Mortgagee mortgagee = new Mortgagee();
                                HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                                mortgagee.seq_no=int.Parse(tdList[0].InnerText.Trim());
                                mortgagee.name=tdList[1].InnerText.Trim();
                                mortgagee.identify_type = tdList[2].InnerText.Trim();
                                mortgagee.identify_no = tdList[3].InnerText.Trim();
                                mortgagees.Add(mortgagee);
                            }

                        }
                        mortgage.mortgagees = mortgagees;
                    }
                    else if (header.StartsWith("抵押物概况"))
                    {
                        List<Guarantee> guarantees = new List<Guarantee>();
                        if (trList != null && trList.Count > 2)
                        {
                            for (int i = 2; i < trList.Count; i++)
                            {
                                Guarantee guarantee = new Guarantee();
                                trList[i].InnerHtml = "\r" + trList[i].InnerHtml;//不加此行selectnodes有问题
                                HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                                guarantee.seq_no = guarantees.Count+1;
                                
                                guarantee.name = tdList[1].InnerText.Trim();
                                guarantee.belong_to = tdList[2].InnerText.Trim();
                                guarantee.desc = tdList[3].InnerText.Trim();
                                guarantee.remarks = tdList[4].InnerText.Trim();
                                guarantees.Add(guarantee);
                            }

                        }
                        mortgage.guarantees = guarantees;
                    }
                }
            }
        }
        #endregion

      
        #region 解析行政处罚
        /// <summary>
        /// 解析行政处罚
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseAdministrativePunishment(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            List<AdministrativePunishment> PunishList = new List<AdministrativePunishment>();

            HtmlNodeCollection tables = rootNode.SelectNodes("//table[@class='result-table']");
            if (tables != null)
            {
                foreach (HtmlNode table in tables)
                {
                    string header = table.SelectNodes("./tr/td")[0].InnerText.Trim();
                    if (header!=null&&header.StartsWith("行政处罚信息"))
                    {
                        HtmlNodeCollection trList = table.SelectNodes("./tr");
                        if (trList != null && trList.Count > 2)
                        {
                            for (int i = 2; i < trList.Count; i++)
                            {
                                HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                                if (tdList != null && tdList.Count > 2)
                                {
                                    AdministrativePunishment punish = new AdministrativePunishment();
                                    punish.seq_no = PunishList.Count+1;
                                    punish.number = tdList[1].InnerText.Trim();
                                    punish.illegal_type = tdList[2].InnerText.Trim();
                                    punish.content = tdList[3].InnerText.Trim();
                                    punish.department = tdList[4].InnerText.Trim();
                                    punish.date = tdList[5].InnerText.Trim();
                                    punish.public_date = tdList[6].InnerText.Trim();
                                    PunishList.Add(punish);
                                    var dtl=tdList[6].SelectSingleNode("./a");
                                    if (dtl != null)
                                    {
                                        var ID = dtl.Attributes["onclick"].Value.Split('(')[1].Split(',')[0].Replace("'","").ToString().Trim();
                                        // 解析行政处罚详情
                                        if (!string.IsNullOrEmpty(ID))
                                        {
                                            var request = CreateRequest();
                                            request.AddOrUpdateRequestParameter("ID", ID);
                                            List<ResponseInfo> reponseList = request.GetResponseInfo(_requestXml.GetRequestListByName("punish_detials"));
                                            if (reponseList.Count() > 0)
                                            {
                                                LoadAndParseAdministrativePunishmentDetails(punish, reponseList[0].Data);
                                            }
                                        }
                                    }
                                }

                            }
                        }
                    }
                }
            }
            _enterpriseInfo.administrative_punishments = PunishList;
        }
        /// <summary>
        /// 解析惩罚详细信息
        /// </summary>
        /// <param name="punish"></param>
        /// <param name="responseData"></param>
        private void LoadAndParseAdministrativePunishmentDetails(AdministrativePunishment punish, String responseData)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);

            HtmlNode rootNode = document.DocumentNode;
            HtmlNode infoTable = rootNode.SelectSingleNode("//table[@class='result-table']");
            if (infoTable != null)
            {
                HtmlNodeCollection trList = infoTable.SelectNodes("./tr");

                if (trList != null && trList.Count == 11)
                {
                    for (int i = 3; i < 6; i++)
                    {
                        HtmlNodeCollection tdList = trList[i].SelectNodes("./td");

                        for (int j = 0; j < tdList.Count; j++)
                        {
                            switch (tdList[j].InnerText.Trim())
                            {
                                case "名称":
                                    punish.name = tdList[j + 1].InnerText.Trim();
                                    break;
                                case "注册号":
                                    punish.reg_no = tdList[j + 1].InnerText.Trim();
                                    break;
                                case "法定代表人（负责人）姓名":
                                    punish.oper_name = tdList[j + 1].InnerText.Trim();
                                    break;
                            }
                        }
                    }
                    punish.description = trList[10].SelectNodes("./td")[0].InnerHtml.ToString();
                }
            }
        }
        #endregion

        private void LoadAndParseStockFreeze(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            List<JudicialFreeze> list = new List<JudicialFreeze>();
            HtmlNode table = rootNode.SelectSingleNode("//table[@class='result-table']");
            HtmlNodeCollection trList = table.SelectSingleNode("./tbody").SelectNodes("./tr");
            if (trList != null && trList.Count > 1)
            {
                for (int i = 1; i < trList.Count; i++)
                {
                    HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                    if (tdList != null && tdList.Count > 6)
                    {
                        JudicialFreeze item = new JudicialFreeze();
                        item.seq_no = list.Count + 1;
                        item.be_executed_person = tdList[1].InnerText;
                        item.amount = tdList[2].InnerText;
                        item.executive_court = tdList[3].InnerText;
                        item.number = tdList[4].InnerText;
                        item.status = tdList[5].InnerText;
                        list.Add(item);
                        var value = tdList[6].InnerHtml.Split('\'')[1];
                        var request = CreateRequest();
                        request.AddOrUpdateRequestParameter("id", value);
                        List<ResponseInfo> reponseList = request.GetResponseInfo(_requestXml.GetRequestListByName("stockfreezeDetail"));
                        LoadAndParseStockFreezeDetail(reponseList[0].Data, item);
                    }
                }
            }
            _enterpriseInfo.judicial_freezes = list;
        }

        private void LoadAndParseStockFreezeDetail(string responseData, JudicialFreeze item)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;
            List<JudicialFreeze> list = new List<JudicialFreeze>();
            HtmlNodeCollection tables = rootNode.SelectNodes("//table[@class='detailsList']");
            foreach (var table in tables)
            {
                HtmlNodeCollection trList = table.SelectNodes("./tr");
                if (trList.Count > 0)
                {
                    if (trList[0].InnerText.Contains("冻结情况"))
                    {
                        LoadAndParseFreezeDetail(item, trList);
                    }
                    else if (trList[0].InnerText.Contains("解冻情况"))
                    {
                        LoadAndParseUnFreezeDetail(item, trList);
                    }
                }
            }
        }

        private void LoadAndParseFreezeDetail(JudicialFreeze item, HtmlNodeCollection trList)
        {
            if (trList != null && trList.Count > 1)
            {
                JudicialFreezeDetail freeze = new JudicialFreezeDetail();
                for (int i = 1; i < trList.Count; i++)
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
                                    freeze.freeze_amount = tdList[j].InnerText.Trim();
                                    break;
                                case "被执行人证件种类":
                                    freeze.assist_ident_type = tdList[j].InnerText.Trim();
                                    break;
                                case "被执行人证件号码":
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

        private void LoadAndParseUnFreezeDetail(JudicialFreeze item, HtmlNodeCollection trList)
        {
            if (trList != null && trList.Count > 1)
            {
                JudicialUnFreezeDetail unfreeze = new JudicialUnFreezeDetail();
                for (int i = 1; i < trList.Count; i++)
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
                                    unfreeze.freeze_amount = tdList[j].InnerText.Trim();
                                    break;
                                case "被执行人证件种类":
                                    unfreeze.assist_ident_type = tdList[j].InnerText.Trim();
                                    break;
                                case "被执行人证件号码":
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
            }
        }

        /// <summary>
        /// 解析年报
        /// </summary>
        /// <param name="responseData"></param>
        /// <param name="_enterpriseInfo"></param>
        private void LoadAndParseReports(string responseData, EnterpriseInfo _enterpriseInfo)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            List<Report> reportList = new List<Report>();

            HtmlNodeCollection tables = rootNode.SelectNodes("//table[@class='result-table']");
            if (tables != null)
            {
                foreach (HtmlNode table in tables)
                {
                    string header = table.SelectNodes("./tr/td")[0].InnerText.Trim();
                    if (header.StartsWith("年报信息"))
                    {
                         HtmlNodeCollection trList = table.SelectSingleNode("./tbody[@id='table2']").SelectNodes("./tr");
                         if (trList != null && trList.Count > 1)
                         {
                             for (int i = 1; i < trList.Count; i++)
                             {
                                 HtmlNodeCollection tdList = trList[i].SelectNodes("./td");
                                 if (tdList != null && tdList.Count > 2)
                                 {
                                     var aNode=tdList[3].SelectSingleNode("./a");
                                     if(aNode==null) return;
                                     Report report = new Report();
                                     string year = tdList[1].InnerText.Length > 4 ? tdList[1].InnerText.Substring(0, 4) : "";
                                     report.ex_id = "";
                                     report.report_name = tdList[1].InnerText.Trim();
                                     report.report_year = year;
                                     report.report_date = tdList[2].InnerText.Trim();
                                     if (!reportsNeedToLoad.Any() || reportsNeedToLoad.Contains(report.report_year))
                                     {
                                         reportList.Add(report);
                                     }
                                     
                                 }

                             }
                         }
                    }
                }
            }
            _enterpriseInfo.reports = reportList;
        }

        /// <summary>
        /// 解析年报Detail
        /// </summary>
        /// <param name="responseInfoList"></param>
        /// <param name="report"></param>
        private void ParseReportInfo(List<ResponseInfo> responseInfoList, Report report)
        {
            foreach (ResponseInfo responseInfo in responseInfoList)
            {
                switch (responseInfo.Name)
                {
                    case "reportDetail":
                        LoadAndParseReportDetail(responseInfo.Data, report);
                        break;
                    default:
                        break;
                }
            }
        }

        private void LoadAndParseReportDetail(string responseData, Report report)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(responseData);
            HtmlNode rootNode = document.DocumentNode;

            HtmlNodeCollection tables = rootNode.SelectNodes("//table[@class='detailsList']");
            if (tables != null)
            {
                foreach (HtmlNode table in tables)
                {
                    string header = table.SelectNodes("./tr/th")[0].InnerText.Trim();
                    if (header != null && header.EndsWith("年度年报报告"))
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
                                            report.reg_no = tdList[i].InnerText.Trim();
                                            break;
                                        case "统一社会信用代码":
                                            report.credit_no = tdList[i].InnerText.Trim();
                                            break;
                                        case "注册号/统一社会信用代码":
                                        case "统一社会信用代码/注册号":
                                            if (tdList[i].InnerText.Trim().Length == 18)
                                                report.credit_no = tdList[i].InnerText.Trim();
                                            else if (tdList[i].InnerText.Trim().Length <= 15)
                                                report.reg_no = tdList[i].InnerText.Trim();
                                            break;
                                        case "注册号统一社会信用代码":
                                        case "营业执照注册号统一社会信用代码":
                                            string[] templist = tdList[i].InnerText.Trim().Trim(new char[] { '\r', '\n', ' ' }).Split('\r');
                                            string temp1 = templist[0].Trim(new char[] { '\n', ' ' });
                                            string temp2 = "";
                                            if (templist.Length > 1)
                                            {
                                                temp2 = templist[1].Trim(new char[] { '\n', ' ' });
                                                if ("无".Equals(temp2)) {
                                                    temp2 = "";
                                                }
                                            }
                                            if (temp1.Length == 18)
                                                report.credit_no = temp1;
                                            else
                                                report.reg_no = temp1;
                                            if (!string.IsNullOrEmpty(temp2))
                                            {
                                                if (temp2.Length == 18)
                                                    report.credit_no = temp2;
                                                else
                                                    report.reg_no = temp2;
                                            }
                                            break;
                                        case "企业名称":
                                        case "名称":
                                            report.name = tdList[i].InnerText.Trim().Replace("&amp;#8226;", "•").Replace("&#8226;", "•");
                                            break;
                                        case "企业联系电话":
                                        case "联系电话":
                                            report.telephone = tdList[i].InnerText.Trim();
                                            break;
                                        case "企业通信地址":
                                            report.address = tdList[i].InnerText.Trim();
                                            break;
                                        case "邮政编码":
                                            report.zip_code = tdList[i].InnerText.Trim();
                                            break;
                                        case "电子邮箱":
                                            report.email = tdList[i].InnerText.Trim();
                                            break;
                                        case "企业是否有投资信息或购买其他公司股权":
                                        case "企业是否有对外投资设立企业信息":
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
                                        case "资金数额":
                                            report.reg_capi = tdList[i].InnerText.Trim();
                                            break;
                                        case "经营者姓名":
                                            report.oper_name = tdList[i].InnerText.Trim();
                                            break;
                                        default:
                                            break;
                                    }
                                }
                            }
                        }
                    }
                    else if (header != null && header.EndsWith("网站或网店信息"))
                    {
                        HtmlNodeCollection trList = table.SelectNodes("./tr");

                        List<WebsiteItem> itemList = new List<WebsiteItem>();
                        int i = 1;
                        foreach (HtmlNode rowNode in trList)
                        {
                            HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                            if (tdList != null && tdList.Count > 2)
                            {
                                WebsiteItem item = new WebsiteItem();

                                item.seq_no = i++;
                                item.web_type = tdList[0].InnerText.Trim();
                                item.web_name = tdList[1].InnerText.Trim();
                                item.web_url = tdList[2].InnerText.Trim();

                                itemList.Add(item);
                            }
                        }

                        report.websites = itemList;
                    }
                    else if (header.EndsWith("发起人及出资信息") || header.EndsWith("股东及出资信息"))
                    {
                        HtmlNodeCollection trList = table.SelectNodes("./tr");

                        List<Partner> itemList = new List<Partner>();
                        foreach (HtmlNode rowNode in trList)
                        {
                            HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                            if (tdList != null && tdList.Count > 6)
                            {
                                Partner item = new Partner()
                                {
                                    identify_no = "",
                                    identify_type = "",
                                    stock_percent = "",
                                    ex_id = "",
                                    stock_name = tdList[0].InnerText.Trim(),
                                    seq_no = report.partners.Count() + 1
                                };

                                ShouldCapiItem sItem = new ShouldCapiItem();
                                sItem.shoud_capi = tdList[1].InnerText.Trim();
                                sItem.should_capi_date = tdList[2].InnerText.Contains("printDate(") ? "" : tdList[2].InnerText.Trim();
                                sItem.invest_type = tdList[3].InnerText.Trim();
                                item.should_capi_items.Add(sItem);

                                RealCapiItem rItem = new RealCapiItem();
                                rItem.real_capi = tdList[4].InnerText.Trim();
                                rItem.real_capi_date = tdList[5].InnerText.Contains("printDate(") ? "" : tdList[5].InnerText.Trim();
                                rItem.invest_type = tdList[6].InnerText.Trim();
                                item.real_capi_items.Add(rItem);

                                if (!itemList.Contains(item))
                                {
                                    itemList.Add(item); 
                                }
                            }
                        }
                        report.partners = itemList;
                    }
                    #region 对外投资信息
                    else if (header != null && header.EndsWith("对外投资信息"))
                    {
                        HtmlNodeCollection trList = table.SelectNodes("./tr");

                        List<InvestItem> itemList = new List<InvestItem>();
                        int i = 1;
                        foreach (HtmlNode rowNode in trList)
                        {
                            HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                            if (tdList != null && tdList.Count > 1)
                            {
                                InvestItem item = new InvestItem();

                                item.seq_no = i++;
                                item.invest_name = tdList[0].InnerText;
                                item.invest_reg_no = tdList[1].InnerText;

                                itemList.Add(item);
                            }
                        }
                        report.invest_items = itemList;
                    }
                    #endregion

                    #region 企业资产状况信息
                    else if (header != null && (header.EndsWith("企业资产状况信息") || header.EndsWith("生产经营情况") || header.StartsWith("生产经营情况")))
                    {
                        HtmlNodeCollection trList = table.SelectNodes("./tr");
                        if (trList!=null&&trList.Count>0)
                        {
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
                    }
                    #endregion

                    #region 发起人股权变更信息
                    else if (header != null && header.EndsWith("发起人股权变更信息"))
                    {
                        HtmlNodeCollection trList = table.SelectNodes("./tr");

                        List<StockChangeItem> ListChange = new List<StockChangeItem>();
                        foreach (HtmlNode rowNode in trList)
                        {
                            HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                            if (tdList != null)
                            {
                                StockChangeItem item = new StockChangeItem();
                                item.seq_no = ListChange.Count+1;
                                item.name = tdList[0].InnerText.Trim();
                                item.before_percent = tdList[1].InnerText.Trim();
                                item.after_percent = tdList[2].InnerText.Trim();
                                item.change_date = tdList[3].InnerText.Trim().Contains("printDate(") ? "" : tdList[3].InnerText.Trim();
                                ListChange.Add(item);
                            }
                        }
                        report.stock_changes = ListChange;
                    }
                    #endregion

                    #region 修改记录
                    else if (header != null && header.EndsWith("修改记录"))
                    {
                        HtmlNodeCollection trList = table.SelectNodes("./tr");

                        List<UpdateRecord> urList = new List<UpdateRecord>();
                        foreach (HtmlNode rowNode in trList)
                        {
                            HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                            if (tdList != null)
                            {
                                UpdateRecord item = new UpdateRecord();
                                item.seq_no = urList.Count + 1;
                                item.update_item = tdList[1].InnerText.Trim();
                                item.before_update = tdList[2].InnerText.Trim();
                                item.after_update = tdList[3].InnerText.Trim();
                                item.update_date = tdList[4].InnerText.Trim();
                                urList.Add(item);
                            }
                        }
                        report.update_records = urList;
                    }
                    #endregion

                    #region 对外提供保证担保信息
                    else if (header != null && header.StartsWith("对外提供保证担保信息"))
                    {
                        HtmlNodeCollection trList = table.SelectNodes("./tr");
                        foreach (HtmlNode rowNode in trList)
                        {
                            HtmlNodeCollection tdList = rowNode.SelectNodes("./td");
                            if (tdList != null&&tdList.Count>7)
                            {
                                ExternalGuarantee item = new ExternalGuarantee();
                                item.seq_no = report.external_guarantees.Count + 1;
                                item.creditor = tdList[0].InnerText.Trim();
                                item.debtor = tdList[1].InnerText.Trim();
                                item.type = tdList[2].InnerText.Trim();
                                item.amount = tdList[3].InnerText.Trim();
                                item.period = tdList[4].InnerText.Trim().Contains("printDate(") ? "" : tdList[4].InnerText.Trim();;
                                item.guarantee_time = tdList[5].InnerText.Trim().Contains("printDate(") ? "" : tdList[5].InnerText.Trim();
                                item.guarantee_type = tdList[6].InnerText.Trim();
                                item.guarantee_scope = tdList[7].InnerText.Trim();

                                report.external_guarantees.Add(item);
                            }
                        }
                       
                    }
                    #endregion
                }
            }
        }

        /// <summary>
        /// 从列表中按名称搜索股东，没有则新建一个
        /// </summary>
        /// <param name="name"></param>
        /// <param name="itemList"></param>
        /// <returns></returns>
        private Partner getInvestItemByNameFormList(string name, List<Partner> itemList)
        {
            foreach(Partner item in itemList)
            {
                if (item.stock_name==name)
                {
                    return item;
                }
            }

            return new Partner();
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
        #region ConvertStringToDate
        private string ConvertStringToDate(string timespan)
        {
            try
            {
                DateTime dt = new DateTime(1970, 1, 1, 23, 59, 59, 999);
                var date = dt.AddMilliseconds(double.Parse(timespan));

                return date.ToString("yyyy年MM月dd日");
            }
            catch
            {
                return string.Empty;
            }
        }
        #endregion
    }
}
