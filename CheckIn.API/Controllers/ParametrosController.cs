using CheckIn.API.Models;
using CheckIn.API.Models.ModelCliente;
using Newtonsoft.Json;
using S22.Imap;
using SAPbobsCOM;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Xml.Linq;

namespace CheckIn.API.Controllers
{
    [Authorize]
    public class ParametrosController: ApiController
    {
        ModelCliente db;
        G G = new G();

        [Route("api/Parametros/Consultar")]
        public HttpResponseMessage GetOne([FromUri]int id)
        {
            try
            {
                G.AbrirConexionAPP(out db);
                
                var Params = db.Parametros.FirstOrDefault();



                G.CerrarConexionAPP(db);
                return Request.CreateResponse(HttpStatusCode.OK, Params);

            }
            catch (Exception ex)
            {
                G.CerrarConexionAPP(db);
                return Request.CreateResponse(HttpStatusCode.InternalServerError, ex);
            }
        }

        [HttpPut]
        [Route("api/Parametros/Actualizar")]
        public HttpResponseMessage Put([FromBody] Parametros param)
        {
            try
            {
                G.AbrirConexionAPP(out db);

                var Rol = db.Parametros.FirstOrDefault();

                if (Rol != null)
                {
                    db.Entry(Rol).State = EntityState.Modified;
                    Rol.SetearManual = param.SetearManual;
                    Rol.Mes = param.Mes;
                    db.SaveChanges();

                }
                else
                {
                    throw new Exception("Parametro no existe");
                }
                G.CerrarConexionAPP(db);
                return Request.CreateResponse(HttpStatusCode.OK, Rol);
            }
            catch (Exception ex)
            {
                G.CerrarConexionAPP(db);
                return Request.CreateResponse(HttpStatusCode.InternalServerError, ex);
            }
        }


    }
}