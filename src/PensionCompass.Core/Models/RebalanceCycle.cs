namespace PensionCompass.Core.Models;

/// <summary>
/// 사용자가 계획하는 다음 리밸런싱까지의 시간 간격. AI에게 보유 펀드의 변동성 허용치와
/// 카탈로그의 5개 수익률 컬럼 중 어느 기간을 더 비중 있게 볼지에 대한 신호로 사용됩니다.
/// 1개월은 펀드 평가 기간으로 노이즈가 너무 크고, 1년 초과는 사실상 buy-and-hold에 가까워져
/// 리밸런싱 의미가 약하기 때문에 의도적으로 3·6·12개월 세 가지로만 제한했습니다.
/// </summary>
public enum RebalanceCycle
{
    ThreeMonths,
    SixMonths,
    OneYear,
}
