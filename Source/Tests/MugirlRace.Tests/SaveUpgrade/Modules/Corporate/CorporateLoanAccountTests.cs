using System;
using Mugirl.Features.Corporate;
using Mugirl.Mathematics;

namespace Mugirl.Tests.SaveUpgrade.Modules.Corporate;

public sealed class CorporateLoanAccountTests
{
    [Fact]
    public void LoanBalancePreservesTheFloatBoundaryThatFixedPointWouldChange()
    {
        var loan = new CorporateLoanAccount { principal = 315.0001f };
        Assert.Equal(315, loan.Balance);
        Assert.Equal(BitConverter.SingleToInt32Bits(315.0001f), BitConverter.SingleToInt32Bits(loan.principal));
    }

    [Fact]
    public void InterestAndRepaymentMatchThePreRefactorFloatOperationsBitForBit()
    {
        var random = new Random(731);
        for (int i = 0; i < 2000; i++)
        {
            var loan = new CorporateLoanAccount
            {
                principal = (float)(random.NextDouble() * 10000),
                interest = (float)(random.NextDouble() * 1000),
                overdueInterestTotal = (float)(random.NextDouble() * 100),
                originalPrincipal = 15000,
                dailyRate = 0.02f,
                overdueDailyRate = 0.03f,
                lastAccrualTick = 73,
                dueTick = 12 * CorporateNetwork.DayTicks + 73,
            };
            int now = random.Next(0, 60 * CorporateNetwork.DayTicks);
            int fullDays = Math.Max(0, (now - loan.lastAccrualTick) / CorporateNetwork.DayTicks);
            int normalDays = Math.Min(fullDays, Math.Max(0, (loan.dueTick - loan.lastAccrualTick) / CorporateNetwork.DayTicks));
            float principal = loan.principal;
            float interest = loan.interest;
            float cumulative = loan.overdueInterestTotal;
            if (fullDays > 0)
            {
                interest += principal * loan.dailyRate * normalDays;
                float overdue = UnityMathf.Min(UnityMathf.Max(0f, loan.originalPrincipal - cumulative), principal * loan.overdueDailyRate * (fullDays - normalDays));
                interest += overdue;
                cumulative += overdue;
            }

            loan.AccrueInterest(now);
            Assert.Equal(BitConverter.SingleToInt32Bits(interest), BitConverter.SingleToInt32Bits(loan.interest));
            Assert.Equal(BitConverter.SingleToInt32Bits(cumulative), BitConverter.SingleToInt32Bits(loan.overdueInterestTotal));
            Assert.Equal(73 + fullDays * CorporateNetwork.DayTicks, loan.lastAccrualTick);
            int amount = random.Next(0, 17000);
            float paidInterest = UnityMathf.Min(amount, interest);
            interest -= paidInterest;
            principal = UnityMathf.Max(0f, principal - (amount - paidInterest));
            loan.ApplyPayment(amount);
            Assert.Equal(BitConverter.SingleToInt32Bits(interest), BitConverter.SingleToInt32Bits(loan.interest));
            Assert.Equal(BitConverter.SingleToInt32Bits(principal), BitConverter.SingleToInt32Bits(loan.principal));
        }
    }
}
