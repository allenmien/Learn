using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data;
using System.Configuration;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using iOubo.iSpider.Model;
using iOubo.iSpider.Model.Common;
using iOubo.iSpider.DataAccess.Mongo;
using iOubo.iSpider.Infrastructure.Business;
using iOubo.iSpider.Infrastructure.Extractor;
using iOubo.iSpider.Common;
using System.Text.RegularExpressions;
using iOubo.iSpider.DataAccess.SqlServer;
using MongoDB.Bson;
using System.Web;

namespace iOubo.iSpider.Infrastructure.Parser
{
    public class DataParser
    {
        OpsHelper opsHelper = new OpsHelper();
        DataHandler dataHandler = new DataHandler();
        DomainJudge domain = new DomainJudge();
        Enterprise enterprise = new Enterprise();
        MainExtractor extractor = new MainExtractor();
        private bool _isUseQGGS = "Y".Equals(ConfigurationManager.AppSettings.Get("IsUseQGGS"));

        public string RequestSingleData(RequestInfo requestInfo)
        {
            string returnString = "";
            try
            {
                //1.请求数据，并解析成enterprise 对象
                IConverter converter = null;
                if (_isUseQGGS)
                {
                    converter = (new ConverterFactory()).CreateConverter("QG");
                }
                else
                {
                    converter=(new ConverterFactory()).CreateConverter(requestInfo.Province);
                }
                
                SummaryEntity entity = converter.ProcessRequestAndParse(requestInfo);

                //如果注册号为空，则表示抓取数据失败，返回空值。
                if ((String.IsNullOrWhiteSpace(entity.Enterprise.reg_no) && String.IsNullOrWhiteSpace(entity.Enterprise.credit_no)))
                {
                    //log into detail error table
                    Console.WriteLine("No reg no nor credit no.. Request params->[" + requestInfo.RegNo + "]");
                    LogHelper.Info("No reg no nor credit no.. Request params->[" + requestInfo.RegNo + "]");
                    LogHelper.Info(String.Format("DetailRequestError, {0}: insert datalog into DataLogs.", requestInfo.RegNo));
                    //InsertErrorDataLog(requestInfo);

                    return "";
                }

                //如果使用注册号抓取工商信息时，没有抓取到注册号，则将搜索的注册号关键字付给reg_no
                if (requestInfo.Province!="BJ" && string.IsNullOrEmpty(entity.Enterprise.reg_no) && !string.IsNullOrEmpty(requestInfo.RegNo) && DataHandler.isRegNo(requestInfo.RegNo))
                {
                    entity.Enterprise.reg_no = requestInfo.RegNo;
                }


                //按组织机构码查询到的企业，直接给组织机构代码赋值
                if (!String.IsNullOrWhiteSpace(requestInfo.OrgNo))
                {
                    entity.Enterprise.org_no = requestInfo.OrgNo;
                }

                //从社会信用代码里提取组织机构代码
                if (String.IsNullOrWhiteSpace(entity.Enterprise.org_no))
                {
                    if (entity.Enterprise.credit_no != null && entity.Enterprise.credit_no.Length == 18)
                    {
                        entity.Enterprise.org_no = entity.Enterprise.credit_no.Substring(8, 9);
                    }
                }

                if (entity.Enterprise.name.Replace("*","").Length <= 4)
                {
                    Console.WriteLine("The length of company name is less then 4 -> " + entity.Enterprise.name + "-" + entity.Enterprise.reg_no);
                    LogHelper.Info("The length of company name is less then 4.. -> " + entity.Enterprise.name + "-" + entity.Enterprise.reg_no + ", Parameters->" + requestInfo.Parameters);
                    //return "";
                }

                if (entity.Enterprise.name.Replace("*", "").Length <= 4)
                {
                    Console.WriteLine("The length of company name is less then 4 -> " + entity.Enterprise.name + "-" + entity.Enterprise.reg_no);
                    LogHelper.Info("The length of company name is less then 4.. -> " + entity.Enterprise.name + "-" + entity.Enterprise.reg_no + ", Parameters->" + requestInfo.Parameters);
                    //return "";
                }
            
                //if partner only has total,automatilly add the detail
                GenerateParnterDetails(entity.Enterprise);
                //if paratner don't have currency ,use the register capi currency
                ApplyRegiCapiCurrency(entity.Enterprise);
                var neweid = entity.Enterprise.eid;

                //更新eid到数据库和阿里云 move to R2M
                //extractor.SetPartnersBranchesWithId(entity.Enterprise);
                //抓取历史名称，联系方式，历史股东，行业、从业人数
                extractor.ExtractData(entity.Enterprise);

                //LogHelper.Info("SaveDataToMongoDBAndUploadDocumentToAliYun - start");
                //2. 保存数据进mongodb, 并且upload 数据至云搜索
                SaveDataToMongoDBAndUploadDocumentToAliYun(entity);
                //LogHelper.Info("SaveDataToMongoDBAndUploadDocumentToAliYun - end");

                //异步提取股东Id, 并更新进数据库
                //Task.Factory.StartNew(() => { extractor.UpdateEIDForPartnerAndBranch(entity.Enterprise, neweid); });

                //3.返回jason格式数据字串
                entity.Enterprise.abnormal_items.Clear();
                entity.Enterprise.checkup_items.Clear();
                returnString = JsonConvert.SerializeObject(entity.Enterprise);
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<string, string> kvp in requestInfo.Parameters)
                {
                    sb.Append(String.Format("{0}={1};", kvp.Key, kvp.Value));
                }

                LogHelper.Error(String.Format("数据请求或解析失败：[Province] = {0},[RegNo]={1},[Parameters] = {2}", requestInfo.Province,HttpUtility.UrlDecode(requestInfo.RegNo), sb), ex);
                //log into detail error table
                //InsertErrorDataLog(requestInfo);

                return String.Empty;
            }

            return returnString;
        }

        private void GenerateParnterDetails(EnterpriseInfo info)
        {
            if(info.partners!=null)
            {
                foreach(var partner in info.partners)
                {
                    RemoveEmptyPartnerDetails(partner);
                    double? totalShould = Utility.GetNumber(partner.total_should_capi);
                    if (totalShould.HasValue && totalShould.Value>0)
                    {
                        if(partner.should_capi_items!=null && partner.should_capi_items.Count==0)
                        {
                            partner.should_capi_items.Add(new ShouldCapiItem { invest_type = string.Empty, shoud_capi = partner.total_should_capi.ToString(), should_capi_date = string.Empty });
                        }
                    }

                    double? totalReal = Utility.GetNumber(partner.total_real_capi);
                    if (totalReal.HasValue && totalReal.Value > 0)
                    {
                        if (partner.real_capi_items != null && partner.real_capi_items.Count == 0)
                        {
                            partner.real_capi_items.Add(new RealCapiItem { invest_type = string.Empty,  real_capi  = partner.total_real_capi.ToString(), real_capi_date = string.Empty });
                        }
                    }
                }
            }
        }

        private void RemoveEmptyPartnerDetails(Partner partner)
        {
            if (partner != null && partner.should_capi_items != null && partner.should_capi_items.Count > 0)
            {
                for (int index = partner.should_capi_items.Count - 1; index >= 0; index--)
                {
                    double? should = Utility.GetNumber(partner.should_capi_items[index].shoud_capi);
                    if (!should.HasValue)
                    {
                        partner.should_capi_items.Remove(partner.should_capi_items[index]);
                    }
                }


            }
            if (partner != null && partner.real_capi_items != null && partner.real_capi_items.Count > 0)
            {
                for (int index = partner.real_capi_items.Count - 1; index >= 0; index--)
                {
                    double? real = Utility.GetNumber(partner.real_capi_items[index].real_capi);
                    if (!real.HasValue)
                    {
                        partner.real_capi_items.Remove(partner.real_capi_items[index]);
                    }
                }
            }
        }

        private void ApplyRegiCapiCurrency(EnterpriseInfo info)
        {
            if (string.IsNullOrWhiteSpace(info.regist_capi))
                return;
            string capi = info.regist_capi.Replace("。", ".").Replace(",", "").Replace("，", "").Replace(" ", "").Replace(" ", "").Replace(".", "").Replace("&nbsp;","");
            string currency = Regex.Replace(capi, @"\b\d+|[0-9]*\.?[0-9]+\b", "");
            if(info.partners!=null)
            {
                foreach(var partner in info.partners)
                {
                    decimal totalShouldValue = 0;
                    if (partner.total_should_capi != null && decimal.TryParse(partner.total_should_capi.Trim(), out totalShouldValue))
                    {
                        partner.total_should_capi = partner.total_should_capi + currency;
                    }
                    decimal totalRealValue = 0;
                    if (partner.total_real_capi != null && decimal.TryParse(partner.total_real_capi.Trim(), out totalRealValue))
                    {
                        partner.total_real_capi = partner.total_real_capi + currency;
                    }

                    if(partner.should_capi_items!= null)
                    {
                        foreach(var scapi in partner.should_capi_items)
                        {
                            
                            decimal value = 0;
                            if (scapi.shoud_capi != null && decimal.TryParse(scapi.shoud_capi.Trim(), out value))
                            {
                                scapi.shoud_capi = scapi.shoud_capi + currency;
                            }
                        }
                    }
                    if (partner.real_capi_items != null)
                    {
                        foreach (var rcapi in partner.real_capi_items)
                        {
                            decimal value = 0;
                            if (rcapi.real_capi != null && decimal.TryParse(rcapi.real_capi.Trim(), out value))
                            {
                                rcapi.real_capi = rcapi.real_capi + currency;
                            }
                        }
                    }
                }
            }

            if(info.financial_contributions!= null)
            {
                foreach (var fc in info.financial_contributions)
                {
                    if (fc.should_capi_items != null)
                    {
                        foreach (var scapi in fc.should_capi_items)
                        {
                            decimal value = 0;
                            if (scapi.should_capi != null && decimal.TryParse(scapi.should_capi.Trim(), out value))
                            {
                                scapi.should_capi = scapi.should_capi + currency;
                            }
                        }
                    }
                    if (fc.real_capi_items != null)
                    {
                        foreach (var rcapi in fc.real_capi_items)
                        {
                            decimal value = 0;
                            if (rcapi.real_capi != null && decimal.TryParse(rcapi.real_capi.Trim(), out value))
                            {
                                rcapi.real_capi = rcapi.real_capi + currency;
                            }
                        }
                    }
                }

            }
            if (info.reports != null)
            {
                foreach (var report in info.reports)
                {
                    if (report.partners != null)
                    {
                        foreach (var fc in report.partners)
                        {
                            if (fc.should_capi_items != null)
                            {
                                foreach (var scapi in fc.should_capi_items)
                                {
                                    decimal value = 0;
                                    if (scapi.shoud_capi != null && decimal.TryParse(scapi.shoud_capi.Trim(), out value))
                                    {
                                        scapi.shoud_capi = scapi.shoud_capi + currency;
                                    }
                                }
                            }
                            if (fc.real_capi_items != null)
                            {
                                foreach (var rcapi in fc.real_capi_items)
                                {
                                    decimal value = 0;
                                    if (rcapi.real_capi != null && decimal.TryParse(rcapi.real_capi.Trim(), out value))
                                    {
                                        rcapi.real_capi = rcapi.real_capi + currency;
                                    }
                                }
                            }
                        }

                    }
                }
            }

        }

        private static void InsertErrorDataLog(RequestInfo requestInfo)
        {
            //try
            //{
            //    DataLogInfo logInfo = new DataLogInfo("", "", requestInfo.Province, "DetailRequestError", requestInfo.Parameters);
            //    MongoDataLogDA daLog = new MongoDataLogDA();
            //    daLog.Insert(logInfo);
            //}
            //catch (Exception ex)
            //{
            //    LogHelper.Error("InsertErrorDataLog with error.", ex);
            //}
        }

        public void SaveDataToMongoDBAndUploadDocumentToAliYun(SummaryEntity entity, int retryCount = 0)
        {
            try
            {
                MongoEnterpriseDA daMongo = new MongoEnterpriseDA();
                if ((entity.Enterprise.status == "已注销" || entity.Enterprise.status == "已吊销") && string.IsNullOrWhiteSpace(entity.Enterprise.econ_kind))
                {
                    var tuple = daMongo.FindEnterpriseInfoByNameAndProvince(entity.Enterprise.name, entity.Enterprise.reg_no, entity.Enterprise.province);
                    if (tuple.Item1 && tuple.Item2 != null)
                    {
                        var info = tuple.Item2;
                        info.status = entity.Enterprise.status;
                        entity.Enterprise = info;
                    }
                    
                }
                //如果reg_no为空，则把社会信用代号赋给它
                if (String.IsNullOrWhiteSpace(entity.Enterprise.reg_no))
                    entity.Enterprise.reg_no = entity.Enterprise.credit_no;

                //if (entity.Enterprise != null && entity.Enterprise.reports != null)
                //{
                //    foreach (var report in entity.Enterprise.reports)
                //    {
                //        if (String.IsNullOrWhiteSpace(report.reg_no))
                //            report.reg_no = report.credit_no;
                //    }
                //}

                //1.1 将Abnormal数据插入mongodb,同时更新ref id in EnterpriseInfo.
                if (!((entity.Enterprise.reg_no == "320502000094652" || entity.Enterprise.name == "苏州尚诚知识产权代理有限公司") && entity.Enterprise.province == Constant.Province.JiangSu))
                {
                    foreach (AbnormalInfo item in entity.Abnormals)
                    {
                        item.province = entity.Enterprise.province;
                        item.reg_no = entity.Enterprise.reg_no;
                        item.name = entity.Enterprise.name;

                        //entity.Enterprise.abnormal_items.Add(new RefItem(daMongo.UpsertAbnormal(item)));
                        entity.Enterprise.abnormal_dtl_items.Add(item);
                    }
                }


                //1.2 将Checkup数据插入mongodb,同时更新ref id in EnterpriseInfo.
                foreach (CheckupInfo item in entity.Checkups)
                {
                    item.province = entity.Enterprise.province;
                    item.reg_no = entity.Enterprise.reg_no;
                    item.name = entity.Enterprise.name;

                    //entity.Enterprise.checkup_items.Add(new RefItem(daMongo.UpsertCheckup(item)));
                    entity.Enterprise.checkup_dtl_items.Add(item);
                }
                entity.Enterprise.addresses=entity.Enterprise.addresses.OrderByDescending(x => x.date).ToList<Address>();
                    
                //获取经纬度
                 List<double> location_xy = enterprise.GetLocationXY(entity.Enterprise.addresses, this.GetCityCodeFromCreditOrRegNo(entity.Enterprise.credit_no, entity.Enterprise.reg_no));
                 List<double> location_xy_gd = enterprise.GetLocationXY_GD(entity.Enterprise.addresses, this.GetCityCodeFromCreditOrRegNo(entity.Enterprise.credit_no, entity.Enterprise.reg_no));
                if (location_xy.Count > 1)
                {
                    entity.Enterprise.latitude =  location_xy[0];
                    entity.Enterprise.longitude = location_xy[1];
                    Console.WriteLine(string.Format("longitude:{0},latitude:{1}", entity.Enterprise.longitude, entity.Enterprise.latitude));
                    //Console.WriteLine(String.Format("find location:{0},{1}",subField.longitude,subField.latitude));
                }
                if (location_xy_gd.Count > 1)
                {
                    entity.Enterprise.gd_longitude = location_xy_gd[0];
                    entity.Enterprise.gd_latitude = location_xy_gd[1];
                    Console.WriteLine(string.Format("gd_longitude:{0},gd_latitude:{1}", entity.Enterprise.gd_longitude, entity.Enterprise.gd_latitude));
                    //Console.WriteLine(String.Format("find location:{0},{1}",subField.longitude,subField.latitude));
                }
                //2 将数据插入mongodb 以及更新主表数据
                string id = daMongo.UpsertEnterprise(entity.Enterprise);
                //eid 为空不上传云
                if (String.IsNullOrWhiteSpace(id)) return;

                //3.上传数据到阿里云搜索
                if (ConfigurationManager.AppSettings["IsUploadToOpsRealtime"] == "Y" && enterprise.UploadDocumentToAliYun(entity.Enterprise))
                {
                    //4.更新ops_flag=1
                    daMongo.UpdateEnterpriseOpsFlagById(id);
                }
                Console.WriteLine("Success for SaveDataToMongoDBAndUploadDocumentToAliYun in retry times-" + retryCount);
            }
            catch (Exception ex)
            {
                int thisRetryCount = retryCount + 1;
                if (thisRetryCount > 5)
                {
                    Console.WriteLine("Retry DB save-" + thisRetryCount);
                    SaveDataToMongoDBAndUploadDocumentToAliYun(entity, thisRetryCount);
                }
                else
                {
                    LogHelper.Error(String.Format("数据Save DB失败：[Province] = {0},[RegNo] = {1},[Name] = {2}", entity.Enterprise.province, entity.Enterprise.reg_no, entity.Enterprise.name), ex);
                    LogHelper.Error("Exceed 5 time for DB error.");
                    throw;
                }
            }
        }

        string GetCityCodeFromCreditOrRegNo(string creditno, string regno)
        {
            var result = string.Empty;
            var tempCreditno = creditno
                .Replace("\r", "")
                .Replace("\n", "")
                .Replace("\t", "")
                .Replace("&nbsp;","")
                .Replace(" ","");
            var tempRegno = regno
                .Replace("\r", "")
                .Replace("\n", "")
                .Replace("\t", "")
                .Replace("&nbsp;", "")
                .Replace(" ", "");
            if (!string.IsNullOrWhiteSpace(tempRegno) && tempRegno.Length == 15)
            {
                result = tempRegno.Substring(0, 4);
            }
            else if (!string.IsNullOrWhiteSpace(tempCreditno) && tempCreditno.Length == 18)
            {
                result = tempCreditno.Substring(2,4);
            }
           
            return result;
        }

    }
}
