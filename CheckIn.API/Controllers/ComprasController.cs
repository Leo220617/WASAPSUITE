using CheckIn.API.Models;
using CheckIn.API.Models.ModelCliente;
using S22.Imap;
using SAPbobsCOM;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
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
    public class ComprasController : ApiController
    {
        ModelCliente db;
        G G = new G();
        Conexion g = new Conexion();
        public string GuardaImagenBase64(string ImagenBase64, string CarpetaImagen, string NomImagen, System.Drawing.Imaging.ImageFormat FormatoImagen)
        {
            Parametros Params = db.Parametros.FirstOrDefault();

            string NombreImagen = "";
            string rutaImagen = "";

            if (NomImagen == "")
            {
                NombreImagen = "NoImage.png";
            }

            //NombreImagen = $"{PrefijoImagen}_{NomImagen}";
            Random i = new Random();
            int o = i.Next(0, 10000);
            NombreImagen = o + "_" + NomImagen;

            var _bytes = Convert.FromBase64String(ImagenBase64);
            string pathImage = $"~/Temp/{G.ObtenerCedulaJuridia()}/{NombreImagen}";
            var fullpath = System.Web.HttpContext.Current.Server.MapPath(pathImage);
            using (System.Drawing.Image image = System.Drawing.Image.FromStream(new MemoryStream(_bytes)))
            {
                try
                {
                    image.Save(fullpath, FormatoImagen);  // aqui seria en base al tipo de imagen

                }catch(Exception ex)
                {
                    G.GuardarTxt("ErrorImagen.txt", ex.ToString());
                }
                image.Save(fullpath, FormatoImagen);
                //image.Save(fullpath, System.Drawing.Imaging.ImageFormat.Png);  // aqui seria en base al tipo de imagen

            }
            rutaImagen = Params.UrlImagenesApp + pathImage;
            rutaImagen = rutaImagen.Replace("~/Temp/", "");

            return NombreImagen;
        }

        [Route("api/Compras/RealizarLecturaEmail")]

        public async Task<HttpResponseMessage> GetRealizarLecturaEmailsAsync()
        {
            try
            {
                G.AbrirConexionAPP(out db);

                var Parametros = db.Parametros.FirstOrDefault();
                var Correos = db.CorreosRecepcion.ToList();

                foreach(var item in Correos)
                {


                using (ImapClient client = new ImapClient(item.RecepcionHostName, (int)(item.RecepcionPort),
                           item.RecepcionEmail, item.RecepcionPassword, AuthMethod.Login, (bool)(item.RecepcionUseSSL)))
                {
                    IEnumerable<uint> uids = client.Search(SearchCondition.Unseen());

                    DateTime recepcionUltimaLecturaImap = DateTime.Now;
                    if (item.RecepcionUltimaLecturaImap != null)
                        recepcionUltimaLecturaImap = item.RecepcionUltimaLecturaImap.Value;

                    uids.Concat(client.Search(SearchCondition.SentSince(recepcionUltimaLecturaImap)));

                    foreach (var uid in uids)
                    {
                        System.Net.Mail.MailMessage message = client.GetMessage(uid);

                        if (message.Attachments.Count > 0)
                        {
                            try
                            {
                                byte[] ByteArrayPDF = null;
                                    int i = 1;
                                   
                                            decimal idGeneral = 0;
                                foreach (var attachment in message.Attachments)
                                {
                                        
                                    try
                                    {
                                        System.IO.StreamReader sr = new System.IO.StreamReader(attachment.ContentStream);
                                     


                                        string texto = sr.ReadToEnd();

                                        if (texto.Substring(0, 3) == "???")
                                            texto = texto.Substring(3);

                                        if(texto.Contains("PDF"))
                                        {

                                            ByteArrayPDF = ((MemoryStream)attachment.ContentStream).ToArray();
                                            //ByteArrayPDF = G.Zip(texto);


                                        }
                                        

                                        if (texto.Contains("FacturaElectronica")
                                                && !texto.Contains("TiqueteElectronico")
                                                && !texto.Contains("NotaCreditoElectronica")
                                                && !texto.Contains("NotaDebitoElectronica"))
                                        {
                                            var emailByteArray = G.Zip(texto);

                                            decimal id = db.Database.SqlQuery<decimal>("Insert Into BandejaEntrada(XmlFactura, Procesado, Asunto, Remitente,Pdf) " +
                                                    " VALUES (@EmailJson, 0, @Asunto, @Remitente, @Pdf); SELECT SCOPE_IDENTITY(); ",
                                                    new SqlParameter("@EmailJson", emailByteArray),
                                                    new SqlParameter("@Asunto", message.Subject),
                                                    new SqlParameter("@Remitente", message.From.ToString()),
                                                    new SqlParameter("@Pdf",(ByteArrayPDF == null ? new byte[0]: ByteArrayPDF))).First();
                                                idGeneral = id;
                                            try
                                            {

                                                var datos = G.ObtenerDatosXmlRechazado(texto);

                                                db.Database.ExecuteSqlCommand("Update BandejaEntrada set NumeroConsecutivo=@NumeroConsecutivo, " +
                                                    " TipoDocumento = @TipoDocumento, FechaEmision = @FechaEmision , " +
                                                    " NombreEmisor = @NombreEmisor,IdEmisor = @IdEmisor ,CodigoMoneda = @CodigoMoneda , " +
                                                    " TotalComprobante = @TotalComprobante " +
                                                    " WHERE Id=@Id ",
                                                     new SqlParameter("@NumeroConsecutivo", datos.NumeroConsecutivo),
                                                     new SqlParameter("@TipoDocumento", datos.TipoDocumento),
                                                     new SqlParameter("@FechaEmision", datos.FechaEmision),
                                                     new SqlParameter("@NombreEmisor", datos.NombreEmisor),
                                                     new SqlParameter("@IdEmisor", datos.Numero),
                                                     new SqlParameter("@CodigoMoneda", datos.CodigoMoneda),
                                                     new SqlParameter("@TotalComprobante", datos.TotalComprobante),
                                                     new SqlParameter("@Id", id));
                                            }
                                            catch { }
                                        }

                                        if(i == message.Attachments.Count())
                                            {
                                                if(idGeneral > 0)
                                                {
                                                    var bandeja = db.BandejaEntrada.Where(a => a.Id == idGeneral).FirstOrDefault();

                                                    if(bandeja.Pdf.Count() ==  0)
                                                    {
                                                            db.Database.ExecuteSqlCommand("Update BandejaEntrada set Pdf=@Pdf " +
                                                       
                                                       " WHERE Id=@Id ",
                                                        new SqlParameter("@Pdf", ByteArrayPDF),
                                                        
                                                        new SqlParameter("@Id", idGeneral));
                                                    }

                                                }
                                            }

                                            i++;
                                        }
                                    catch (Exception ex)
                                    {


                                    }
                                }
                            }
                            catch (Exception ex)
                            {


                            }
                        }
                        message.Dispose();

                        await System.Threading.Tasks.Task.Delay(100);
                    }
                        db.Entry(item).State = EntityState.Modified;
                    item.RecepcionUltimaLecturaImap = DateTime.Now;
                        db.SaveChanges();

                    }

                }


                G.CerrarConexionAPP(db);
                return Request.CreateResponse(HttpStatusCode.OK);
            }
            catch (Exception ex)
            {

                BitacoraErrores be = new BitacoraErrores();
                be.Descripcion = ex.Message;
                be.StackTrace = ex.StackTrace;
                be.Metodo = "Lectura de emails";
                be.Fecha = DateTime.Now;
                db.BitacoraErrores.Add(be);
                db.SaveChanges();

                G.CerrarConexionAPP(db);
                return Request.CreateResponse(HttpStatusCode.InternalServerError, ex);
            }
        }


        [Route("api/Compras/LeerBandejaEntrada")]
        public async Task<HttpResponseMessage> GetLeerBandejaEntradaAsync()
        {
            try
            {
                G.AbrirConexionAPP(out db);

                var Lista = db.BandejaEntrada.Where(a => a.Procesado == "0" && string.IsNullOrEmpty(a.Mensaje)).ToList();

                foreach (var item in Lista)
                {
                    try
                    {
                        var attachmentBody = G.Unzip(item.XmlFactura);
                        EncCompras factura = new EncCompras();
                        string xmlBase64 = attachmentBody;

                        //var BodyPdf = G.Unzip(item.Pdf);
                        string pdfBase64 = "";



                        var xml = G.ConvertirArchivoaXElement(xmlBase64, G.ObtenerCedulaJuridia());
                       

                        if (!xmlBase64.Contains("FacturaElectronica")
                            && !xmlBase64.Contains("TiqueteElectronico")
                            && !xmlBase64.Contains("NotaCreditoElectronica")
                            && !xmlBase64.Contains("NotaDebitoElectronica"))
                            throw new Exception("No es un documento electrónico");

                        factura.ClaveHacienda = G.ExtraerValorDeNodoXml(xml, "Clave");
                        factura.ConsecutivoHacienda = G.ExtraerValorDeNodoXml(xml, "NumeroConsecutivo");
                        factura.FecFactura = DateTime.Parse(G.ExtraerValorDeNodoXml(xml, "FechaEmision"));
                        factura.CodigoActividadEconomica = G.ExtraerValorDeNodoXml(xml, "CodigoActividad");
                        factura.FechaGravado = DateTime.Now;
                        factura.CodEmpresa = G.ObtenerCedulaJuridia();

                        try
                        {
                            factura.NumFactura = int.Parse(factura.ConsecutivoHacienda.Substring(10, 10));
                        }
                        catch (Exception ex)
                        {
                            factura.NumFactura = int.Parse(factura.ConsecutivoHacienda.Substring(11, 9));

                        }
                       
                        factura.TipoDocumento = factura.ConsecutivoHacienda.Substring(8, 2);
                        if (factura.TipoDocumento == "04")
                            throw new Exception($"El documento es un Tiquete Electrónico, que no puede ser utilizado como gasto deducible. Debe solicitar al proveedor que genere una Factura Electrónica.");


                        //Informacion del Proveedor o emisor de la factura
                        //       factura.TipoIdentificacionProveedor = G.ExtraerValorDeNodoXml(xml, "Emisor/Identificacion/Tipo");
                        factura.CodProveedor = G.ExtraerValorDeNodoXml(xml, "Emisor/Identificacion/Numero");

                        // si el nombre se pasa de 80 caracteres debemos cortarlo

                        if (db.EncCompras.Where(m => m.CodEmpresa == factura.CodEmpresa
                   && m.CodProveedor == factura.CodProveedor
                   && m.NumFactura == factura.NumFactura
                   && m.TipoDocumento == factura.TipoDocumento).Count() > 0)
                        {
                            throw new Exception($"El documento ya existe [Clave={factura.ClaveHacienda}] [Consecutivo={factura.ConsecutivoHacienda}]");
                        }

                        //Información del Cliente o Receptor de la factura
                        factura.TipoIdentificacionCliente = G.ExtraerValorDeNodoXml(xml, "Receptor/Identificacion/Tipo");
                        factura.CodCliente = G.ExtraerValorDeNodoXml(xml, "Receptor/Identificacion/Numero");
                        factura.NomCliente = G.ExtraerValorDeNodoXml(xml, "Receptor/Nombre");
                        if (factura.NomCliente.Length > 50)
                            factura.NomCliente = factura.NomCliente.Substring(0, 50);
                        factura.EmailCliente = G.ExtraerValorDeNodoXml(xml, "Receptor/CorreoElectronico");

                        factura.CondicionVenta = G.ExtraerValorDeNodoXml(xml, "CondicionVenta");

                        if(factura.CodCliente != factura.CodEmpresa)
                        {
                            throw new Exception($"El documento no fue dirigido para esta compañia [Empresa={factura.CodEmpresa}] [Cliente de Factura={factura.CodCliente}]");
                        }

                        try
                        {
                            factura.DiasCredito = int.Parse(G.ExtraerValorDeNodoXml(xml, "PlazoCredito", true));
                        }
                        catch
                        {
                            factura.DiasCredito = 0;
                        }

                        factura.MedioPago = G.ExtraerValorDeNodoXml(xml, "MedioPago");
                        if (attachmentBody.Contains("xml-schemas/v4.3"))
                        {
                            factura.CodMoneda = G.ExtraerValorDeNodoXml(xml, "ResumenFactura/CodigoTipoMoneda/CodigoMoneda");
                            //  factura.TipoCA = decimal.Parse(G.ExtraerValorDeNodoXml(xml, "ResumenFactura/CodigoTipoMoneda/TipoCambio", true));
                        }
                        else
                        {
                            factura.CodMoneda = G.ExtraerValorDeNodoXml(xml, "ResumenFactura/CodigoMoneda");
                            //factura.TipoCambio = decimal.Parse(G.ExtraerValorDeNodoXml(xml, "ResumenFactura/TipoCambio", true));
                        }

                        if (string.IsNullOrWhiteSpace(factura.CodMoneda))
                        {
                            factura.CodMoneda = "CRC";
                            //factura.TipoCambio = 1;
                        }

                        factura.TotalServGravados = decimal.Parse(G.ExtraerValorDeNodoXml(xml, "ResumenFactura/TotalServGravados", true));
                        factura.TotalServExentos = decimal.Parse(G.ExtraerValorDeNodoXml(xml, "ResumenFactura/TotalServExentos", true));
                        factura.TotalMercanciasGravadas = decimal.Parse(G.ExtraerValorDeNodoXml(xml, "ResumenFactura/TotalMercanciasGravadas", true));
                        factura.TotalMercanciasExentas = decimal.Parse(G.ExtraerValorDeNodoXml(xml, "ResumenFactura/TotalMercanciasExentas", true));
                        //factura.TotalGravado = decimal.Parse(G.ExtraerValorDeNodoXml(xml, "ResumenFactura/TotalGravado", true));
                        factura.TotalServExonerado = decimal.Parse(G.ExtraerValorDeNodoXml(xml, "ResumenFactura/TotalServExonerado", true));
                        factura.TotalMercExonerada = decimal.Parse(G.ExtraerValorDeNodoXml(xml, "ResumenFactura/TotalMercExonerada", true));
                        factura.TotalExonerado = decimal.Parse(G.ExtraerValorDeNodoXml(xml, "ResumenFactura/TotalExonerado", true));
                        factura.TotalIVADevuelto = decimal.Parse(G.ExtraerValorDeNodoXml(xml, "ResumenFactura/TotalIVADevuelto", true));
                        factura.TotalOtrosCargos = decimal.Parse(G.ExtraerValorDeNodoXml(xml, "OtrosCargos/MontoCargo", true));

                        factura.TotalExento = decimal.Parse(G.ExtraerValorDeNodoXml(xml, "ResumenFactura/TotalExento", true));
                        factura.TotalVenta = decimal.Parse(G.ExtraerValorDeNodoXml(xml, "ResumenFactura/TotalVenta", true));
                        factura.TotalDescuentos = decimal.Parse(G.ExtraerValorDeNodoXml(xml, "ResumenFactura/TotalDescuentos", true));
                        factura.TotalVentaNeta = decimal.Parse(G.ExtraerValorDeNodoXml(xml, "ResumenFactura/TotalVentaNeta", true));
                        factura.TotalImpuesto = decimal.Parse(G.ExtraerValorDeNodoXml(xml, "ResumenFactura/TotalImpuesto", true));
                        factura.TotalComprobante = decimal.Parse(G.ExtraerValorDeNodoXml(xml, "ResumenFactura/TotalComprobante", true));

                        var NomProveedor = G.ExtraerValorDeNodoXml(xml, "Emisor/Nombre");
                        factura.XmlFacturaRecibida = G.StringToBase64(xmlBase64);
                        factura.NomProveedor = NomProveedor;
                        var pdfResp = G.GuardarPDF(item.Pdf, G.ObtenerCedulaJuridia(), factura.NumFactura);

                        factura.PdfFactura = pdfResp;
                        factura.PdfFac = item.Pdf;
                        decimal iva1 = 0;
                        decimal iva2 = 0;
                        decimal iva4 = 0;
                        decimal iva8 = 0;
                        decimal iva13 = 0;

                        List<DetCompras> detCpmpras = new List<DetCompras>();
                        foreach (var item2 in xml.Elements().Where(m => m.Name.LocalName == "DetalleServicio").Elements())
                        {
                            var det = new DetCompras();
                            det.CodEmpresa = factura.CodEmpresa;
                            det.NumFactura = factura.NumFactura;
                            det.CodProveedor = factura.CodProveedor;
                            det.TipoDocumento = factura.TipoDocumento;
                            det.ClaveHacienda = factura.ClaveHacienda;
                            det.ConsecutivoHacienda = factura.ConsecutivoHacienda;
                            det.NomProveedor = NomProveedor;
                            det.NumLinea = short.Parse(G.ExtraerValorDeNodoXml(item2, "NumeroLinea"));

                            if (attachmentBody.Contains("xml-schemas/v4.3"))
                            {
                                det.CodPro = G.ExtraerValorDeNodoXml(item2, "CodigoComercial/Codigo");
                                if (det.CodPro.Length > 20)
                                    det.CodPro = det.CodPro.Substring(0, 20);
                            }
                            else
                            {
                                det.CodPro = G.ExtraerValorDeNodoXml(item2, "Codigo/Codigo");
                                if (det.CodPro.Length > 20)
                                    det.CodPro = det.CodPro.Substring(0, 20);
                            }

                            det.NomPro = G.ExtraerValorDeNodoXml(item2, "Detalle");
                            det.CodCabys = G.ExtraerValorDeNodoXml(item2, "Codigo");
                            if (det.NomPro.Length > 60)
                                det.NomPro = det.NomPro.Substring(0, 60);


                            det.UnidadMedida = G.ExtraerValorDeNodoXml(item2, "UnidadMedida");
                            var Decimal = Convert.ToDecimal(G.ExtraerValorDeNodoXml(item2, "Cantidad", true));
                            det.Cantidad = Convert.ToInt32(Decimal);
                            det.PrecioUnitario = decimal.Parse(G.ExtraerValorDeNodoXml(item2, "PrecioUnitario", true));
                            det.MontoTotal = decimal.Parse(G.ExtraerValorDeNodoXml(item2, "MontoTotal", true));
                            //det.NaturalezaDescuento = G.ExtraerValorDeNodoXml(item2.Elements().Where(a => a.Name.LocalName == "Descuento").FirstOrDefault(), "NaturalezaDescuento");
                            det.MontoDescuento = decimal.Parse(G.ExtraerValorDeNodoXml(item2.Elements().Where(a => a.Name.LocalName == "Descuento").FirstOrDefault(), "MontoDescuento", true));
                            det.SubTotal = decimal.Parse(G.ExtraerValorDeNodoXml(item2, "SubTotal", true));

                            //Impuesto
                            // det.ImpuestoCodigo = G.ExtraerValorDeNodoXml(item2, "Impuesto/Codigo");
                            det.ImpuestoTarifa = decimal.Parse(G.ExtraerValorDeNodoXml(item2, "Impuesto/Tarifa", true));
                            det.ImpuestoMonto = decimal.Parse(G.ExtraerValorDeNodoXml(item2, "Impuesto/Monto", true));

                            ////exoneracion
                            //det.ExoneracionTipoDocumento = G.ExtraerValorDeNodoXml(item2, "Impuesto/Exoneracion/TipoDocumento");
                            //if (!string.IsNullOrEmpty(det.ExoneracionTipoDocumento))
                            //{
                            //    det.ExoneracionNumeroDocumento = G.ExtraerValorDeNodoXml(item2, "Impuesto/Exoneracion/NumeroDocumento");
                            //    det.ExoneracionNombreInstitucion = G.ExtraerValorDeNodoXml(item2, "Impuesto/Exoneracion/NombreInstitucion");
                            //    det.ExoneracionFechaEmision = DateTime.Parse(G.ExtraerValorDeNodoXml(item2, "Impuesto/Exoneracion/FechaEmision"));
                            //    det.ExoneracionMontoImpuesto = decimal.Parse(G.ExtraerValorDeNodoXml(item2, "Impuesto/Exoneracion/MontoImpuesto", true));
                            //    det.ExoneracionPorcentajeCompra = decimal.Parse(G.ExtraerValorDeNodoXml(item2, "Impuesto/Exoneracion/PorcentajeCompra", true));
                            //}

                            det.idTipoGasto = EncontrarGasto(db, det.CodCabys);


                            det.MontoTotalLinea = decimal.Parse(G.ExtraerValorDeNodoXml(item2, "MontoTotalLinea", true));
                            

                           var ExoneracionPorcentajeCompra = decimal.Parse(G.ExtraerValorDeNodoXml(item2, "Impuesto/Exoneracion/PorcentajeCompra", true));

                            int opcion = Convert.ToInt32(det.ImpuestoTarifa);
                            decimal cantidadImpuesto = 0;
                            bool bandera = false;
                            if (ExoneracionPorcentajeCompra > 0)
                            {
                                bandera = true;
                                cantidadImpuesto = opcion - ExoneracionPorcentajeCompra;
                            }
                            switch (opcion)
                            {
                                case 1:
                                    {
                                        if (!bandera)
                                        {
                                            iva1 += det.ImpuestoMonto.Value;
                                        }
                                        else
                                        {
                                            if (cantidadImpuesto > 0)
                                            {
                                                iva1 += ((det.SubTotal.Value - det.MontoDescuento.Value) * (cantidadImpuesto / 100));
                                            }
                                        }
                                        break;
                                    }
                                case 2:
                                    {
                                        if (!bandera)
                                        {
                                            iva2 += det.ImpuestoMonto.Value;
                                        }
                                        else
                                        {
                                            if (cantidadImpuesto > 0)
                                            {
                                                iva2 += ((det.SubTotal.Value - det.MontoDescuento.Value) * (cantidadImpuesto / 100));
                                            }
                                        }
                                        break;
                                    }
                                case 4:
                                    {
                                        if (!bandera)
                                        {
                                            iva4 += det.ImpuestoMonto.Value;

                                        }
                                        else
                                        {
                                            if (cantidadImpuesto > 0)
                                            {
                                                iva4 += ((det.SubTotal.Value - det.MontoDescuento.Value) * (cantidadImpuesto / 100));
                                            }
                                        }
                                        break;
                                    }
                                case 8:
                                    {
                                        if (!bandera)
                                        {
                                            iva8 += det.ImpuestoMonto.Value;
                                        }
                                        else
                                        {
                                            if (cantidadImpuesto > 0)
                                            {
                                                iva8 += ((det.SubTotal.Value - det.MontoDescuento.Value) * (cantidadImpuesto / 100));
                                            }
                                        }
                                        break;
                                    }
                                case 13:
                                    {
                                        if (!bandera)
                                        {
                                            iva13 += det.ImpuestoMonto.Value;
                                        }
                                        else
                                        {
                                            if (cantidadImpuesto > 0)
                                            {
                                                iva13 += ((det.SubTotal.Value - det.MontoDescuento.Value) * (cantidadImpuesto / 100));
                                            }
                                        }
                                        break;
                                    }
                            }


                            db.DetCompras.Add(det);
                            detCpmpras.Add(det);
                        }

                        factura.Impuesto1 = iva1;
                        factura.Impuesto2 = iva2;
                        factura.Impuesto4 = iva4;
                        factura.Impuesto8 = iva8;
                        factura.Impuesto13 = iva13;
                        factura.idCierre = 0;
                        factura.RegimenSimplificado = false;
                        factura.FacturaExterior = false;
                        factura.GastosVarios = false;
                        factura.FacturaNoRecibida = false;
                        factura.Comentario = "";

                        try
                        {
                            string SQL = " Select top 1 t0.LicTradNum , t0.cardcode,t0.CardName, ";
                            SQL += " case when LicTradNum Like '0%' then SUBSTRING( Replace(LicTradNum, '-',''),2,LEN(Replace(LicTradNum, '-','')) - 1)  ";
                            SQL += " else Replace(LicTradNum, '-','') end  Cedula from ocrd t0  where t0.CardType = 's' and case when LicTradNum Like '0%' then SUBSTRING( Replace(LicTradNum, '-',''),2,LEN(Replace(LicTradNum, '-','')) - 1)  ";
                            SQL += " else Replace(LicTradNum, '-','') end = '"+factura.CodProveedor+"' ";

                            Conexion g = new Conexion();
                            SqlConnection Cn = new SqlConnection(g.DevuelveCadena());
                            SqlCommand Cmd = new SqlCommand(SQL, Cn);
                            SqlDataAdapter Da = new SqlDataAdapter(Cmd);
                            DataSet Ds = new DataSet();
                            Cn.Open();
                            Da.Fill(Ds, "Proveedor");

                            factura.CardCode = Ds.Tables["Proveedor"].Rows[0]["cardcode"].ToString();
                            Cn.Close();
                        }
                        catch (Exception ex)
                        {
                            try
                            {
                                Conexion.Desconectar();
                                var client = (SAPbobsCOM.BusinessPartners)Conexion.Company.GetBusinessObject(SAPbobsCOM.BoObjectTypes.oBusinessPartners);
                                client.CardName = factura.NomProveedor;
                                client.Valid = BoYesNoEnum.tYES;
                                client.CardType = BoCardTypes.cSupplier;
                                client.Currency = "##";
                                client.FederalTaxID = factura.CodProveedor;
                                client.Series = 76;
                                client.GroupCode = 101;
                                client.EmailAddress = G.ExtraerValorDeNodoXml(xml, "Emisor/CorreoElectronico");
                                client.Phone1 = G.ExtraerValorDeNodoXml(xml, "Emisor/Telefono/NumTelefono");
                                client.UserFields.Fields.Item("U_DYD_Actividad").Value = G.ExtraerValorDeNodoXml(xml, "CodigoActividad").ToString();


                                var Provincia = Convert.ToInt32(G.ExtraerValorDeNodoXml(xml, "Emisor/Ubicacion/Provincia").ToString());
                                var Canton = Convert.ToInt32(G.ExtraerValorDeNodoXml(xml, "Emisor/Ubicacion/Canton").ToString());
                                var Distrito = Convert.ToInt32(G.ExtraerValorDeNodoXml(xml, "Emisor/Ubicacion/Distrito").ToString());
                                var Barrio = Convert.ToInt32(G.ExtraerValorDeNodoXml(xml, "Emisor/Ubicacion/Barrio").ToString());

                                var C = "";
                                var D = "";
                                var B = "";


                                Conexion g = new Conexion();
                                SqlConnection Cn = new SqlConnection(g.DevuelveCadena());
                                SqlCommand Cmd = new SqlCommand("Select * from Cantones where CodCanton = '" + Canton +"' and CodProvincia = '" + Provincia + "'" , Cn);
                                SqlDataAdapter Da = new SqlDataAdapter(Cmd);
                                DataSet Ds = new DataSet();
                                Cn.Open();
                                Da.Fill(Ds, "Cantones1");
                                  C = Ds.Tables["Cantones1"].Rows[0]["NomCanton"].ToString();

                                Cn.Close();

                                SqlCommand CmdD = new SqlCommand("Select * from Distritos where CodCanton = '" + Canton + "' and CodProvincia = '" + Provincia + "'" + " and CodDistrito = '"+ Distrito +"'", Cn);
                                SqlDataAdapter DaD = new SqlDataAdapter(CmdD);
                                DataSet DsD = new DataSet();
                                Cn.Open();
                                DaD.Fill(DsD, "Distritos1");
                                D = DsD.Tables["Distritos1"].Rows[0]["NomDistrito"].ToString();

                                Cn.Close();


                                SqlCommand CmdB = new SqlCommand("Select * from Barrios where CodCanton = '" + Canton + "' and CodProvincia = '" + Provincia + "'" + " and CodDistrito = '" + Distrito + "' and CodBarrio = '" + Barrio + "'" , Cn);
                                SqlDataAdapter DaB = new SqlDataAdapter(CmdB);
                                DataSet DsB = new DataSet();
                                Cn.Open();
                                DaB.Fill(DsB, "Barrios1");
                                B = DsB.Tables["Barrios1"].Rows[0]["NomBarrio"].ToString();

                                Cn.Close();


                                client.Addresses.Add();
                                client.Addresses.SetCurrentLine(0);



                                client.City = C;
                                client.County = C;
                                client.Block = D;
                                client.Address = G.ExtraerValorDeNodoXml(xml, "Emisor/Ubicacion/OtrasSenas").ToString().Length > 49 ? G.ExtraerValorDeNodoXml(xml, "Emisor/Ubicacion/OtrasSenas").ToString().Substring(0,49) : G.ExtraerValorDeNodoXml(xml, "Emisor/Ubicacion/OtrasSenas").ToString();
                               
                                client.Addresses.AddressName = client.Address;
                                client.Addresses.City = C;
                                client.Addresses.County = C;
                                client.Addresses.State = G.ExtraerValorDeNodoXml(xml, "Emisor/Ubicacion/Provincia").ToString();
                                client.Addresses.Street = G.ExtraerValorDeNodoXml(xml, "Emisor/Ubicacion/OtrasSenas").ToString().Length > 99 ? G.ExtraerValorDeNodoXml(xml, "Emisor/Ubicacion/OtrasSenas").ToString().Substring(0, 99) : G.ExtraerValorDeNodoXml(xml, "Emisor/Ubicacion/OtrasSenas").ToString();
                                client.Addresses.Block = D;

                                client.Addresses.TypeOfAddress = "S";

                                var respuest = client.Add();

                                if (respuest != 0)
                                {
                                    BitacoraErrores error = new BitacoraErrores();
                                    error.Descripcion = Conexion.Company.GetLastErrorDescription();
                                    error.StackTrace = "Insercion del proveedor en la factura " + factura.ConsecutivoHacienda;
                                    error.Fecha = DateTime.Now;
                                    db.BitacoraErrores.Add(error);
                                    db.SaveChanges();
                                    Conexion.Desconectar();
                                    factura.CardCode = "";
                                }else
                                {
                                    try
                                    {
                                        string SQL = " Select t0.LicTradNum , t0.cardcode,t0.CardName, ";
                                        SQL += " case when LicTradNum Like '0%' then SUBSTRING( Replace(LicTradNum, '-',''),2,LEN(Replace(LicTradNum, '-','')) - 1)  ";
                                        SQL += " else Replace(LicTradNum, '-','') end  Cedula from ocrd t0  where t0.CardType = 's' and case when LicTradNum Like '0%' then SUBSTRING( Replace(LicTradNum, '-',''),2,LEN(Replace(LicTradNum, '-','')) - 1)  ";
                                        SQL += " else Replace(LicTradNum, '-','') end = '" + factura.CodProveedor + "' ";

                                        
                                        SqlConnection Cn2 = new SqlConnection(g.DevuelveCadena());
                                        SqlCommand Cmd2 = new SqlCommand(SQL, Cn2);
                                        SqlDataAdapter Da2 = new SqlDataAdapter(Cmd2);
                                        DataSet Ds2 = new DataSet();
                                        Cn2.Open();
                                        Da2.Fill(Ds2, "Proveedor");

                                        factura.CardCode = Ds2.Tables["Proveedor"].Rows[0]["CardCode"].ToString();
                                        Cn2.Close();
                                    }
                                    catch (Exception rr)
                                    {
                                        BitacoraErrores error = new BitacoraErrores();
                                        error.Descripcion = rr.Message + " " + " No se pudo crear el proveedor";
                                        error.StackTrace = "NO se encontro el proveedor en la factura " + factura.ConsecutivoHacienda;
                                        error.Fecha = DateTime.Now;
                                        db.BitacoraErrores.Add(error);
                                        db.SaveChanges();
                                        factura.CardCode = "";
                                    }
                                }

                            }
                            catch (Exception e)
                            {
                                try
                                {
                                    BitacoraErrores error = new BitacoraErrores();
                                    error.Descripcion = e.Message + " " + " al hacer un proveedor " + Conexion.Company.GetLastErrorDescription();
                                    error.StackTrace = e.StackTrace;
                                    error.Fecha = DateTime.Now;

                                    db.BitacoraErrores.Add(error);
                                    db.SaveChanges();
                                    Conexion.Desconectar();

                                }
                                catch (Exception ex4)
                                {

                                     
                                }
                               
                            }
                            

                        }
                     


                        factura.idTipoGasto = detCpmpras.Where(a => a.NumFactura == factura.NumFactura && a.ClaveHacienda == factura.ClaveHacienda && a.ConsecutivoHacienda == factura.ConsecutivoHacienda).FirstOrDefault() == null ? 0: detCpmpras.Where(a => a.NumFactura == factura.NumFactura && a.ClaveHacienda == factura.ClaveHacienda && a.ConsecutivoHacienda == factura.ConsecutivoHacienda).FirstOrDefault().idTipoGasto;
                        db.EncCompras.Add(factura);
                        db.Database.ExecuteSqlCommand("Update BandejaEntrada SET Procesado=1 WHERE Id=@Id",
                           new SqlParameter("@Id", item.Id));
                        db.SaveChanges();
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            string procesado = "1";
                           
                            db.Database.ExecuteSqlCommand("Update BandejaEntrada SET Mensaje=@Mensaje, Procesado=@Procesado WHERE Id=@Id",
                                 new SqlParameter("@Mensaje", ex.Message),
                                 new SqlParameter("@Procesado", procesado),
                                 new SqlParameter("@Id", item.Id));


                            db.SaveChanges();


                        }
                        catch
                        {
                        }

                    }

                }

                G.CerrarConexionAPP(db);
                return Request.CreateResponse(HttpStatusCode.OK);
            }
            catch (Exception ex)
            {


                BitacoraErrores be = new BitacoraErrores();
                be.Descripcion = ex.Message;
                be.StackTrace = ex.StackTrace;
                be.Metodo = "Lectura de Bandeja";
                be.Fecha = DateTime.Now;
                db.BitacoraErrores.Add(be);
                db.SaveChanges();
                G.CerrarConexionAPP(db);
                return Request.CreateResponse(HttpStatusCode.InternalServerError, ex);
            }
        }


        public async Task<HttpResponseMessage> Get([FromUri] Filtros filtro)
        {
            try
            {
                G.AbrirConexionAPP(out db);
                DateTime time = new DateTime();
                var EncCompras = db.EncCompras.Select(a => new
                {
                    a.id,
                    a.CodEmpresa
                 ,
                    a.CodProveedor,
                    a.NomProveedor
                 ,
                    a.TipoDocumento
                 ,
                    a.NumFactura
                 ,
                    a.FecFactura
                 ,
                    a.TipoIdentificacionCliente
                 ,
                    a.CodCliente
                 ,
                    a.NomCliente
                 ,
                    a.EmailCliente
                 ,
                    a.DiasCredito
                 ,
                    a.CondicionVenta
                 ,
                    a.ClaveHacienda
                 ,
                    a.ConsecutivoHacienda
                 ,
                    a.MedioPago
                 ,
                    a.Situacion
                 ,
                    a.CodMoneda
                 ,
                    a.TotalServGravados
                 ,
                    a.TotalServExentos
                 ,
                    a.TotalMercanciasGravadas
                 ,
                    a.TotalMercanciasExentas
                 ,
                    a.TotalExento
                 ,
                    a.TotalVenta
                 ,
                    a.TotalDescuentos
                 ,
                    a.TotalVentaNeta
                 ,
                    a.TotalImpuesto
                 ,
                    a.TotalComprobante
                 ,
                    a.XmlFacturaRecibida
                 
                 ,
                    a.FechaGravado
                 ,
                    a.TotalServExonerado
                 ,
                    a.TotalMercExonerada
                 ,
                    a.TotalExonerado
                 ,
                    a.TotalIVADevuelto
                 ,
                    a.TotalOtrosCargos
                 ,
                    a.CodigoActividadEconomica
                 ,
                    a.idLoginAsignado
                 ,
                    a.FecAsignado
                
                 ,
                    PdfFactura = db.Parametros.FirstOrDefault().UrlImagenesApp + a.PdfFactura
                 ,
                    a.idNormaReparto
                 ,
                    a.idTipoGasto
                 ,
                    a.CardCode
                 ,
                 TipoGasto = (a.idTipoGasto == 0 ? "Sin Asignar":  db.Gastos.Where(z =>z.idTipoGasto == a.idTipoGasto).FirstOrDefault().Nombre),
                    a.idCierre,
                    a.Impuesto1,
                    a.Impuesto2,
                    a.Impuesto4,
                    a.Impuesto8,
                    a.Impuesto13,
                  a.PdfFac,
                  a.Comentario,
                    DetCompras = db.DetCompras.Where(d => d.NumFactura == a.NumFactura && d.TipoDocumento == a.TipoDocumento && d.ClaveHacienda == a.ClaveHacienda && d.ConsecutivoHacienda == a.ConsecutivoHacienda).ToList()

                }).Where(a => (filtro.FechaInicio != time ? a.FecFactura >= filtro.FechaInicio : true)).ToList();

                if (!string.IsNullOrEmpty(filtro.Texto))
                {
                    //filtro.Codigo1 = Convert.ToInt32(filtro.Texto);

                    EncCompras = EncCompras.Where(a => a.ConsecutivoHacienda.ToString().Contains(filtro.Texto.ToUpper()) ||
                    a.ClaveHacienda.ToString().Contains(filtro.Texto.ToUpper())
                    ).ToList();
                }

                if(filtro.FechaInicio != time)
                {
                    filtro.FechaFinal = filtro.FechaFinal.AddDays(1);
                    EncCompras = EncCompras.Where(a => a.FecFactura >= filtro.FechaInicio && a.FecFactura <= filtro.FechaFinal).ToList();
                }
             
                if(filtro.Asignados)

                {
                    if(filtro.Codigo2 > 0)
                    {
                         
                            EncCompras = EncCompras.Where(a => a.idLoginAsignado == null || a.idLoginAsignado == 0 || a.idLoginAsignado == filtro.Codigo2).ToList();

                       

                    }
                    else
                    {
                        EncCompras = EncCompras.Where(a => a.idLoginAsignado == null || a.idLoginAsignado == 0 ).ToList();
                    }
                }

                if(filtro.Codigo3 > 0)
                {
                    //EncCompras = EncCompras.Where(a => a.idCierre == filtro.Codigo3).ToList();
                }
                    





                G.CerrarConexionAPP(db);
                return Request.CreateResponse(HttpStatusCode.OK, EncCompras);

            }
            catch (Exception ex)
            {
                G.CerrarConexionAPP(db);
                return Request.CreateResponse(HttpStatusCode.InternalServerError, ex);
            }
        }

        [Route("api/Compras/Listado")]
        public async Task<HttpResponseMessage> GetComprasModulo([FromUri] Filtros filtro)
        {
            try
            {
                G.AbrirConexionAPP(out db);
                DateTime time = new DateTime();
                var EncCompras = db.EncCompras.Select(a => new
                {
                    a.id,
                    a.CodEmpresa
                 ,
                    a.CodProveedor,
                    a.NomProveedor
                 ,
                    a.TipoDocumento
                 ,
                    a.NumFactura
                 ,
                    a.FecFactura
                 ,
                    a.TipoIdentificacionCliente
                 ,
                    a.CodCliente
                 ,
                    a.NomCliente
                 ,
                    a.EmailCliente
                 ,
                    a.DiasCredito
                 ,
                    a.CondicionVenta
                 ,
                    a.ClaveHacienda
                 ,
                    a.ConsecutivoHacienda
                 ,
                    a.MedioPago
                 ,
                    a.Situacion
                 ,
                    a.CodMoneda
                 ,
                    a.TotalServGravados
                 ,
                    a.TotalServExentos
                 ,
                    a.TotalMercanciasGravadas
                 ,
                    a.TotalMercanciasExentas
                 ,
                    a.TotalExento
                 ,
                    a.TotalVenta
                 ,
                    a.TotalDescuentos
                 ,
                    a.TotalVentaNeta
                 ,
                    a.TotalImpuesto
                 ,
                    a.TotalComprobante
                 ,
                
                    a.FechaGravado
                 ,
                    a.TotalServExonerado
                 ,
                    a.TotalMercExonerada
                 ,
                    a.TotalExonerado
                 ,
                    a.TotalIVADevuelto
                 ,
                    a.TotalOtrosCargos
                 ,
                    a.CodigoActividadEconomica
                 ,
                    a.idLoginAsignado
                 ,
                    a.FecAsignado

                 ,
                    PdfFactura = db.Parametros.FirstOrDefault().UrlImagenesApp + a.PdfFactura
                 ,
                    a.idNormaReparto
                 ,
                    a.idTipoGasto
                 ,
                    TipoGasto = (a.idTipoGasto == 0 ? "Sin Asignar" : db.Gastos.Where(z => z.idTipoGasto == a.idTipoGasto).FirstOrDefault().Nombre),
                    a.idCierre,
                    a.Impuesto1,
                    a.Impuesto2,
                    a.Impuesto4,
                    a.Impuesto8,
                    a.Impuesto13,
                    a.PdfFac,
                    a.RegimenSimplificado,
                    a.FacturaExterior,
                    a.GastosVarios,
                    a.FacturaNoRecibida,
                    a.Comentario,
                    a.CardCode,
                    Usuario = (a.idCierre == 0 ? 0 : db.EncCierre.Where(z => z.idCierre == a.idCierre).FirstOrDefault().idLogin) ,
                    DetCompras = db.DetCompras.Where(d => d.NumFactura == a.NumFactura && d.TipoDocumento == a.TipoDocumento && d.ClaveHacienda == a.ClaveHacienda && d.ConsecutivoHacienda == a.ConsecutivoHacienda).ToList()

                }).Where(a => (filtro.FechaInicio != time ? a.FecFactura >= filtro.FechaInicio : true) && (filtro.NumCierre > 0 ? a.idCierre == filtro.NumCierre: true)).ToList();

                if (!string.IsNullOrEmpty(filtro.Texto))
                {
                    //filtro.Codigo1 = Convert.ToInt32(filtro.Texto);

                    EncCompras = EncCompras.Where(a => a.ConsecutivoHacienda.ToString().Contains(filtro.Texto.ToUpper()) ||
                    a.ClaveHacienda.ToString().Contains(filtro.Texto.ToUpper())  
                   
                    ).ToList();
                }

                if(!string.IsNullOrEmpty(filtro.Texto2))
                {
                    EncCompras = EncCompras.Where(a =>   a.NomProveedor.ToString().ToUpper().Contains(filtro.Texto2.ToUpper())

                   ).ToList();
                }

              
               
                if (filtro.FechaInicio != time)
                {
                    filtro.FechaFinal = filtro.FechaFinal.AddDays(1);
                    EncCompras = EncCompras.Where(a => a.FecFactura >= filtro.FechaInicio && a.FecFactura <= filtro.FechaFinal).ToList();
                }

                if (filtro.Asignados)

                {
                     
                    EncCompras = EncCompras.Where(a => a.idLoginAsignado == null || a.idLoginAsignado == 0).ToList();
                    
                }

                if (!string.IsNullOrEmpty(filtro.CodMoneda) && filtro.CodMoneda != "NULL")
                {
                    EncCompras = EncCompras.Where(a => a.CodMoneda == filtro.CodMoneda).ToList();
                }

                if(filtro.RegimeSimplificado)
                {
                    EncCompras = EncCompras.Where(a => a.RegimenSimplificado == filtro.RegimeSimplificado).ToList();
                }


                if(filtro.FacturaExterior)
                {
                    EncCompras = EncCompras.Where(a => a.FacturaExterior == filtro.FacturaExterior).ToList();
                }

                if(filtro.FacturaNoRecibida)
                {
                    EncCompras = EncCompras.Where(a => a.FacturaNoRecibida == filtro.FacturaNoRecibida).ToList();
                }

                G.CerrarConexionAPP(db);
                return Request.CreateResponse(HttpStatusCode.OK, EncCompras);

            }
            catch (Exception ex)
            {
                G.CerrarConexionAPP(db);
                return Request.CreateResponse(HttpStatusCode.InternalServerError, ex);
            }
        }



        [Route("api/Compras/Consultar")]
        public HttpResponseMessage GetOne([FromUri]int id)
        {
            try
            {
                G.AbrirConexionAPP(out db);


                var Cuentas = db.EncCompras.Where(a => a.id == id).Select(a => new
                {
                    a.id,
                    a.CodEmpresa
                 ,
                    a.CodProveedor
                 ,
                    a.NomProveedor
                 ,
                    a.TipoDocumento
                 ,
                    a.NumFactura
                 ,
                    a.FecFactura
                 ,
                    a.TipoIdentificacionCliente
                 ,
                    a.CodCliente
                 ,
                    a.NomCliente
                 ,
                    a.EmailCliente
                 ,
                    a.DiasCredito
                 ,
                    a.CondicionVenta
                 ,
                    a.ClaveHacienda
                 ,
                    a.ConsecutivoHacienda
                 ,
                    a.MedioPago
                 ,
                    a.Situacion
                 ,
                    a.CodMoneda
                 ,
                    a.TotalServGravados
                 ,
                    a.TotalServExentos
                 ,
                    a.TotalMercanciasGravadas
                 ,
                    a.TotalMercanciasExentas
                 ,
                    a.TotalExento
                 ,
                    a.TotalVenta
                 ,
                    a.TotalDescuentos
                 ,
                    a.TotalVentaNeta
                 ,
                    a.TotalImpuesto
                 ,
                    a.TotalComprobante
                 ,
                    a.XmlFacturaRecibida
                 
 
                 ,
                    a.FechaGravado
                 ,
                    a.TotalServExonerado
                 ,
                    a.TotalMercExonerada
                 ,
                    a.TotalExonerado
                 ,
                    a.TotalIVADevuelto
                 ,
                    a.TotalOtrosCargos
                 ,
                    a.CodigoActividadEconomica
                 ,
                    a.idLoginAsignado
                 ,
                 UsuarioAsignado = db.Login.Where(d => d.id == a.idLoginAsignado).FirstOrDefault() == null ? "": db.Login.Where(d => d.id == a.idLoginAsignado).FirstOrDefault().Nombre,
                    a.FecAsignado
                
                 ,
                    PdfFactura = db.Parametros.FirstOrDefault().UrlImagenesApp + a.PdfFactura
                 ,
                    a.idNormaReparto
                 ,
                    a.idTipoGasto
                 ,
                    TipoGasto = (a.idTipoGasto == 0 ? "Sin Asignar" : db.Gastos.Where(z => z.idTipoGasto == a.idTipoGasto).FirstOrDefault().Nombre),
                    a.idCierre,
                    a.Impuesto1,
                    a.Impuesto2,
                    a.Impuesto4,
                    a.Impuesto8,
                    a.Impuesto13,
                     a.PdfFac,
                     a.Comentario,
                     a.CardCode,
                    DetCompras = db.DetCompras.Where(d => d.NumFactura == a.NumFactura && d.ConsecutivoHacienda == a.ConsecutivoHacienda && d.ClaveHacienda == a.ClaveHacienda).ToList()

                }).FirstOrDefault();


                if (Cuentas == null)
                {
                    throw new Exception("Esta cuenta no se encuentra registrada");
                }
                G.CerrarConexionAPP(db);
                return Request.CreateResponse(HttpStatusCode.OK, Cuentas);
            }
            catch (Exception ex)
            {
                G.CerrarConexionAPP(db);
                return Request.CreateResponse(HttpStatusCode.InternalServerError, ex);
            }
        }

        [HttpPost]
        public HttpResponseMessage Post([FromBody] ComprasViewModel compra)
            
        {
                G.AbrirConexionAPP(out db);
            var t = db.Database.BeginTransaction();
            try
            {

                var EncCompras = db.EncCompras.Where(a => a.id == compra.EncCompras.id).FirstOrDefault();

                if (EncCompras == null)
                {
                    EncCompras = new EncCompras();
                    if(compra.EncCompras.FacturaExterior)
                    {
                        EncCompras.ClaveHacienda = compra.EncCompras.NumFactura.ToString();
                        EncCompras.ConsecutivoHacienda = compra.EncCompras.NumFactura.ToString();
                    }
                    else
                    {

                        EncCompras.ClaveHacienda = compra.EncCompras.ClaveHacienda;
                        EncCompras.ConsecutivoHacienda = compra.EncCompras.ConsecutivoHacienda;
                    }
                    EncCompras.NumFactura = compra.EncCompras.NumFactura;
                    EncCompras.FecFactura = compra.EncCompras.FecFactura;
                    EncCompras.FechaGravado = DateTime.Now;
                    EncCompras.CodProveedor = compra.EncCompras.CodProveedor;
                    EncCompras.NomProveedor = compra.EncCompras.NomProveedor;


                    try
                    {
                        string SQL = " Select top 1 t0.LicTradNum , t0.cardcode,t0.CardName, ";
                        SQL += " case when LicTradNum Like '0%' then SUBSTRING( Replace(LicTradNum, '-',''),2,LEN(Replace(LicTradNum, '-','')) - 1)  ";
                        SQL += " else Replace(LicTradNum, '-','') end  Cedula from ocrd t0  where t0.CardType = 's' and case when LicTradNum Like '0%' then SUBSTRING( Replace(LicTradNum, '-',''),2,LEN(Replace(LicTradNum, '-','')) - 1)  ";
                        SQL += " else Replace(LicTradNum, '-','') end = '" + EncCompras.CodProveedor + "' ";

                        Conexion g = new Conexion();
                        SqlConnection Cn = new SqlConnection(g.DevuelveCadena());
                        SqlCommand Cmd = new SqlCommand(SQL, Cn);
                        SqlDataAdapter Da = new SqlDataAdapter(Cmd);
                        DataSet Ds = new DataSet();
                        Cn.Open();
                        Da.Fill(Ds, "Proveedor");

                        EncCompras.CardCode = Ds.Tables["Proveedor"].Rows[0]["CardCode"].ToString();
                        Cn.Close();

                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            Conexion.Desconectar();
                            var client = (SAPbobsCOM.BusinessPartners)Conexion.Company.GetBusinessObject(SAPbobsCOM.BoObjectTypes.oBusinessPartners);
                            client.CardName = EncCompras.NomProveedor;
                            client.Valid = BoYesNoEnum.tYES;
                            client.CardType = BoCardTypes.cSupplier;
                            client.Currency = "##";
                            client.FederalTaxID = EncCompras.CodProveedor;
                            client.Series = 76;
                            client.GroupCode = 101;
                            client.EmailAddress = "";
                            client.Phone1 = "";
                           

 

                            var respuest = client.Add();

                            if (respuest != 0)
                            {
                                BitacoraErrores error = new BitacoraErrores();
                                error.Descripcion = Conexion.Company.GetLastErrorDescription();
                                error.StackTrace = "Insercion del proveedor en la factura " + EncCompras.ConsecutivoHacienda;
                                error.Fecha = DateTime.Now;
                                db.BitacoraErrores.Add(error);
                                db.SaveChanges();
                                Conexion.Desconectar();
                                EncCompras.CardCode = "";
                            }
                            else
                            {
                                try
                                {
                                    string SQL = " Select top 1 t0.LicTradNum , t0.cardcode,t0.CardName, ";
                                    SQL += " case when LicTradNum Like '0%' then SUBSTRING( Replace(LicTradNum, '-',''),2,LEN(Replace(LicTradNum, '-','')) - 1)  ";
                                    SQL += " else Replace(LicTradNum, '-','') end  Cedula from ocrd t0  where t0.CardType = 's' and case when LicTradNum Like '0%' then SUBSTRING( Replace(LicTradNum, '-',''),2,LEN(Replace(LicTradNum, '-','')) - 1)  ";
                                    SQL += " else Replace(LicTradNum, '-','') end = '" + EncCompras.CodProveedor + "' ";


                                    SqlConnection Cn2 = new SqlConnection(g.DevuelveCadena());
                                    SqlCommand Cmd2 = new SqlCommand(SQL, Cn2);
                                    SqlDataAdapter Da2 = new SqlDataAdapter(Cmd2);
                                    DataSet Ds2 = new DataSet();
                                    Cn2.Open();
                                    Da2.Fill(Ds2, "Proveedor");

                                    EncCompras.CardCode = Ds2.Tables["Proveedor"].Rows[0]["cardcode"].ToString();
                                    Cn2.Close();
                                }
                                catch (Exception rr)
                                {
                                    BitacoraErrores error = new BitacoraErrores();
                                    error.Descripcion = rr.Message + " " + " No se pudo crear el proveedor";
                                    error.StackTrace = "NO se encontro el proveedor en la factura " + EncCompras.ConsecutivoHacienda;
                                    error.Fecha = DateTime.Now;
                                    db.BitacoraErrores.Add(error);
                                    db.SaveChanges();
                                    EncCompras.CardCode = "";
                                }
                            }

                        }
                        catch (Exception e)
                        {
                            try
                            {
                                BitacoraErrores error = new BitacoraErrores();
                                error.Descripcion = e.Message + " " + " al hacer un proveedor " + Conexion.Company.GetLastErrorDescription();
                                error.StackTrace = e.StackTrace;
                                error.Fecha = DateTime.Now;

                                db.BitacoraErrores.Add(error);
                                db.SaveChanges();
                                Conexion.Desconectar();

                            }
                            catch (Exception ex4)
                            {


                            }

                        }
                    }
               

                    EncCompras.CodEmpresa = G.ObtenerCedulaJuridia();
                    EncCompras.CodCliente = compra.EncCompras.CodCliente;
                    EncCompras.NomCliente = compra.EncCompras.NomCliente;
                    EncCompras.CodigoActividadEconomica = compra.EncCompras.CodigoActividadEconomica;
                    EncCompras.CodMoneda = compra.EncCompras.CodMoneda;
                    EncCompras.DiasCredito = compra.EncCompras.DiasCredito;
                    EncCompras.Impuesto1 = compra.EncCompras.Impuesto1;
                    EncCompras.Impuesto2 = compra.EncCompras.Impuesto2;
                    EncCompras.Impuesto4 = compra.EncCompras.Impuesto4;
                    EncCompras.Impuesto8 = compra.EncCompras.Impuesto8;
                    EncCompras.Impuesto13 = compra.EncCompras.Impuesto13;
                    EncCompras.TotalComprobante = compra.EncCompras.TotalComprobante;
                    EncCompras.TotalDescuentos = compra.EncCompras.TotalDescuentos;
                    EncCompras.TotalImpuesto = compra.EncCompras.TotalImpuesto;
                    EncCompras.TotalVenta = compra.EncCompras.TotalVenta;
                    EncCompras.TotalVentaNeta = compra.EncCompras.TotalVentaNeta;
                    EncCompras.TotalOtrosCargos = 0;
                    EncCompras.TipoDocumento = "01";
                    EncCompras.EmailCliente = "";
                    EncCompras.FacturaExterior = compra.EncCompras.FacturaExterior;
                    EncCompras.RegimenSimplificado = compra.EncCompras.RegimenSimplificado;
                    EncCompras.GastosVarios = compra.EncCompras.GastosVarios;
                    EncCompras.FacturaNoRecibida = compra.EncCompras.FacturaNoRecibida;
                    EncCompras.idTipoGasto = compra.DetCompras.FirstOrDefault().idTipoGasto;
                    EncCompras.idCierre = 0;
                    if (!String.IsNullOrEmpty(compra.EncCompras.ImagenBase64))
                    {
                        string Url = GuardaImagenBase64(compra.EncCompras.ImagenBase64, G.ObtenerCedulaJuridia(), G.ObtenerCedulaJuridia() + "_" + EncCompras.ConsecutivoHacienda + ".jpg", System.Drawing.Imaging.ImageFormat.Jpeg);
                        EncCompras.PdfFactura = Url;
                        var _bytes = Convert.FromBase64String(compra.EncCompras.ImagenBase64);
                        EncCompras.PdfFac = _bytes;

                    }
                    else
                    {
                        EncCompras.PdfFactura = EncCompras.PdfFactura;
                    }
                    EncCompras.Comentario = "";
                    db.EncCompras.Add(EncCompras);
                    db.SaveChanges();

                    var i = 1;
                    foreach(var item in compra.DetCompras)
                    {
                        var Det = new DetCompras();
                        Det.CodProveedor = EncCompras.CodProveedor;
                        Det.CodEmpresa = EncCompras.CodEmpresa;
                        Det.TipoDocumento = "01";
                        Det.ClaveHacienda = EncCompras.ClaveHacienda;
                        Det.ConsecutivoHacienda = EncCompras.ConsecutivoHacienda;
                        Det.NomProveedor = EncCompras.NomProveedor;
                        Det.NumFactura = EncCompras.NumFactura;
                        Det.NumLinea = i;
                        Det.CodPro = item.CodPro;
                        Det.UnidadMedida = item.UnidadMedida;
                        Det.NomPro = item.NomPro;
                        Det.PrecioUnitario = item.PrecioUnitario;
                        Det.Cantidad = item.Cantidad;
                        Det.MontoTotal = item.MontoTotal;
                        Det.MontoDescuento = item.MontoDescuento;
                        Det.SubTotal = item.SubTotal;
                        Det.ImpuestoTarifa = item.ImpuestoTarifa;
                        Det.ImpuestoMonto = item.ImpuestoMonto;
                        Det.MontoTotalLinea = item.MontoTotalLinea;
                        var TipoGasto = db.Gastos.Where(a => a.Nombre.ToUpper().Contains("Regimen Simplificado".ToUpper())).FirstOrDefault();

                        if(item.idTipoGasto == 0)
                        {
                            Det.idTipoGasto = TipoGasto.idTipoGasto;
                        }
                        else
                        {
                            Det.idTipoGasto = item.idTipoGasto;

                        }
                        
                       // Det.CodCabys = item.CodCabys;
                        db.DetCompras.Add(Det);
                        db.SaveChanges();
                        i++;
                    }

                    compra.EncCompras.id = EncCompras.id;
                }
                else
                {
                    throw new Exception("Esta factura YA existe");
                }
                t.Commit();
                G.CerrarConexionAPP(db);
                return Request.CreateResponse(HttpStatusCode.OK, compra);
            }
            catch (Exception ex)
            {
                t.Rollback();
                G.CerrarConexionAPP(db);

                G.GuardarTxt("ErrorFactura.txt", ex.ToString());
                return Request.CreateResponse(HttpStatusCode.InternalServerError, ex);
            }
        }

        [Route("api/Compras/Prueba")]

        public HttpResponseMessage GetPrueba()
        {

            try
            {
                G.AbrirConexionAPP(out db);

                var Facturas = db.EncCompras.Where(a => a.PdfFactura == "" || a.PdfFactura == null).ToList();


                foreach(var item in Facturas)
                {
                    var pdfResp = G.GuardarPDF(item.PdfFac, G.ObtenerCedulaJuridia(), item.NumFactura);

                    db.Entry(item).State = EntityState.Modified;

                    item.PdfFactura = pdfResp;

                    db.SaveChanges();
                }

               

                G.CerrarConexionAPP(db);
                return Request.CreateResponse(HttpStatusCode.OK, HttpContext.Current.Server.MapPath("~").ToString());
            }
            catch (Exception ex)
            {

                BitacoraErrores be = new BitacoraErrores();
                be.Descripcion = ex.Message;
                be.StackTrace = ex.StackTrace;
                be.Metodo = "Lectura de Bandeja";
                be.Fecha = DateTime.Now;
                db.BitacoraErrores.Add(be);
                db.SaveChanges();

                G.CerrarConexionAPP(db);

                return Request.CreateResponse(HttpStatusCode.InternalServerError, ex.Message);


            }


           
        }

        [HttpPut]
        [Route("api/Compras/Actualizar")]
        public HttpResponseMessage Put([FromBody] AsignacionViewModel asig)
        {
            try
            {
                G.AbrirConexionAPP(out db);

                var User = db.Login.Where(a => a.id == asig.idLogin).FirstOrDefault();



                if (User != null && User.Activo == true)
                {
                    var Compra = db.EncCompras.Where(a => a.id == asig.idFac).FirstOrDefault();

                    if(Compra == null)
                    {
                        throw new Exception("Compra no existe");
                    }

                    db.Entry(Compra).State = EntityState.Modified;
                    //Compra.idLoginAsignado = asig.idLogin;
                    if(asig.idNorma > 0)
                    {
                        Compra.idNormaReparto = asig.idNorma;
                    }
                    else
                    {
                        
                        Compra.idNormaReparto = db.NormasReparto.Where(a => a.idLogin == asig.idLogin).FirstOrDefault().id;
                    }
                    Compra.FecAsignado = DateTime.Now;

                    db.SaveChanges();

                }
                else
                {
                    throw new Exception("Usuario no existe o está inactivo");
                }
                G.CerrarConexionAPP(db);
                return Request.CreateResponse(HttpStatusCode.OK);
            }
            catch (Exception ex)
            {
                G.CerrarConexionAPP(db);
                G.GuardarTxt("ErrorFactura.txt", ex.ToString());
                return Request.CreateResponse(HttpStatusCode.InternalServerError, ex);
            }
        }




        public int EncontrarGasto(ModelCliente db, string NomPro)
        {
            try
            {


                var Gastos = db.Gastos.ToList();

                foreach (var gasto in Gastos)
                {
                    var palabrasClaves = gasto.PalabrasClave.Split(';').ToList();
                    palabrasClaves.Remove(palabrasClaves[palabrasClaves.Count() - 1]);

                    foreach (var item in palabrasClaves)
                    {
                        if (QuitarTilde(NomPro).ToUpper().Contains(QuitarTilde(item).Replace(" ", string.Empty).ToUpper()))
                        {
                            return gasto.idTipoGasto;
                        }
                    }

                }




                return 0;

            }
            catch (Exception)
            {

                return 0;
            }
        }

        public static string QuitarTilde(string inputString)
        {
            string normalizedString = inputString.Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < normalizedString.Length; i++)
            {
                UnicodeCategory uc = CharUnicodeInfo.GetUnicodeCategory(normalizedString[i]);
                if (uc != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(normalizedString[i]);
                }
            }
            return (sb.ToString().Normalize(NormalizationForm.FormC));
        }
    }
}