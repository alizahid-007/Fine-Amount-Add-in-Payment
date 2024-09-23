using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Fine_Amount_Add_in_Payment
{
    public class CalculateFine : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {

            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);
            // Prevent infinite loops by checking plugin depth.
            if (context.MessageName.ToLower() == "update" && context.Depth > 1)
            {
                return;
            }

            if (context.MessageName.ToLower() == "create")
            {
                ExecuteCreate(context, service);
            }
        }

        private void ExecuteCreate(IPluginExecutionContext context, IOrganizationService service)
        {
            try
            {
                if (context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity)
                {
                    Entity payment = (Entity)context.InputParameters["Target"];

                    if (payment.Contains("mc_voucherno"))
                    {
                        string invoiceNumber = payment["mc_voucherno"].ToString();
                        QueryExpression query = new QueryExpression("mc_feeinvoice");
                        query.ColumnSet.AddColumns("mc_duedate", "mc_paymentdate", "mc_campus", "mc_invoicestatus", "mc_invoicetype");
                        query.Criteria.AddCondition("mc_vouchernumber", ConditionOperator.Equal, invoiceNumber);
                        EntityCollection feeInvoices = service.RetrieveMultiple(query);
                        if (feeInvoices.Entities.Count > 0)
                        {
                            Entity feeInvoice = feeInvoices.Entities[0];
                            OptionSetValue feeInvoiceValue = feeInvoice.GetAttributeValue<OptionSetValue>("mc_invoicetype");
                            // Check if invoicetype is equal to 124540002
                            if (feeInvoiceValue != null && feeInvoiceValue.Value == 124540002)
                            {
                                DateTime paymentDate = DateTime.Now;
                                if (payment.Contains("mc_paymentdate"))
                                {
                                    paymentDate = payment.GetAttributeValue<DateTime>("mc_paymentdate");
                                }
                                if (feeInvoice.Contains("mc_duedate"))
                                {
                                    DateTime dueDate = feeInvoice.GetAttributeValue<DateTime>("mc_duedate");
                                    if (paymentDate > dueDate)
                                    {
                                        int daysLate = (paymentDate - dueDate).Days;
                                        if (feeInvoice.Contains("mc_campus"))
                                        {
                                            EntityReference campusRef = feeInvoice.GetAttributeValue<EntityReference>("mc_campus");
                                            Entity campus = service.Retrieve(campusRef.LogicalName, campusRef.Id, new ColumnSet("mc_fineamount"));
                                            if (campus.Contains("mc_fineamount"))
                                            {
                                                Money fineAmountPerDay = campus.GetAttributeValue<Money>("mc_fineamount");
                                                if (fineAmountPerDay != null)
                                                {
                                                    decimal calculatedFineAmount = daysLate * fineAmountPerDay.Value;
                                                    //if (payment.Contains("mc_fineamount"))
                                                    //{
                                                    Money fineAmount = payment.GetAttributeValue<Money>("mc_fineamount");
                                                    bool fineCalculate = payment.GetAttributeValue<bool>("mc_finecalculate");

                                                    // Check if calculated fine matches the payment fine
                                                    if (fineCalculate == false)
                                                    {
                                                        payment["mc_fineamount"] = new Money(fineAmount.Value);
                                                        // Retrieve the invoice amount and update total amount
                                                        if (payment.Contains("mc_invoiceamount"))
                                                        {
                                                            Money invoiceAmount = payment.GetAttributeValue<Money>("mc_invoiceamount");
                                                            decimal updatedTotalAmount = invoiceAmount.Value + fineAmount.Value;
                                                            payment["mc_totalamount"] = new Money(updatedTotalAmount);
                                                        }
                                                        // Update the payment record
                                                        service.Update(payment);
                                                    }
                                                    else
                                                    {
                                                        payment["mc_fineamount"] = new Money(calculatedFineAmount);
                                                        // Retrieve the invoice amount and update total amount
                                                        if (payment.Contains("mc_invoiceamount"))
                                                        {
                                                            Money invoiceAmount = payment.GetAttributeValue<Money>("mc_invoiceamount");
                                                            decimal updatedTotalAmount = invoiceAmount.Value + calculatedFineAmount;
                                                            payment["mc_totalamount"] = new Money(updatedTotalAmount);
                                                        }
                                                        // Update the payment record
                                                        service.Update(payment);
                                                    }
                                                    //}
                                                }
                                            }
                                        }
                                    }
                                    else if (paymentDate <= dueDate)
                                    {
                                        // Set fine amount to 0 if payment date is equal to due date
                                        payment["mc_fineamount"] = new Money(0);

                                        // Set total amount to the invoice amount
                                        if (payment.Contains("mc_invoiceamount"))
                                        {
                                            Money invoiceAmount = payment.GetAttributeValue<Money>("mc_invoiceamount");
                                            payment["mc_totalamount"] = invoiceAmount;
                                        }
                                        // Update the payment record
                                        service.Update(payment);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidPluginExecutionException("An error occurred in the CalculateFine plugin during create.", ex);
            }

        }

    }
}
